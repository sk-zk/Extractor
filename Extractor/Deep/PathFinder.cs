﻿using Extractor.Properties;
using Sprache;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TruckLib.HashFs;
using TruckLib.Models;
using TruckLib.Sii;
using static Extractor.PathUtils;

namespace Extractor.Deep
{
    /// <summary>
    /// Searches the entries of a HashFS archive for paths contained in it.
    /// </summary>
    internal class PathFinder
    {
        /// <summary>
        /// File paths that were discovered by <see cref="Find"/>.
        /// </summary>
        public HashSet<string> FoundFiles { get; private set; }

        /// <summary>
        /// Decoy file paths that were discovered by <see cref="Find"/>.
        /// Since decoy files typically contain junk data, they are kept separate
        /// from all other discovered paths.
        /// </summary>
        public HashSet<string> FoundDecoyFiles { get; private set; }

        /// <summary>
        /// File types which can be ignored because they don't contain
        /// any path references.
        /// </summary>
        private static readonly FileType[] IgnorableFileTypes = [
            FileType.Dds,
            FileType.Ini,
            FileType.Jpeg,
            FileType.Pma,
            FileType.Pmc,
            FileType.Pmg,
            FileType.Png,
            FileType.Psd,
            FileType.SoundBank,
            FileType.SoundBankGuids,
            FileType.Sui, // handled by the including .sii files
            FileType.TgaMask,
            ];

        /// <summary>
        /// This is used to find the absolute path of tobj files that are referenced in 
        /// mat files for which the mat path was not found and the tobj is referenced as
        /// relative path only.
        /// </summary>
        private static readonly string[] RootsToSearchForRelativeTobj = [
            "/material", "/model", "/model2", "/prefab", "/prefab2", "/vehicle"
        ];
        
        /// <summary>
        /// The HashFS reader.
        /// </summary>
        private IHashFsReader reader;

        /// <summary>
        /// Paths that have been visited so far.
        /// </summary>
        private HashSet<string> visited;

        /// <summary>
        /// Entries that have been visited so far.
        /// </summary>
        private HashSet<IEntry> visitedEntries;

        /// <summary>
        /// Directories discovered during the first phase which might contain tobj files.
        /// This is used to find the absolute path of tobj files that are referenced in 
        /// mat files for which the mat path was not found and the tobj is referenced as
        /// relative path only.
        /// </summary>
        private HashSet<string> dirsToSearchForRelativeTobj;

        public PathFinder(IHashFsReader reader)
        {
            this.reader = reader;
        }

        /// <summary>
        /// Scans the archive for paths and writes the results to <see cref="FoundFiles"/> 
        /// and <see cref="FoundDecoyFiles"/>.
        /// </summary>
        public void Find()
        {
            visited = [];
            visitedEntries = [];
            FoundFiles = [];
            dirsToSearchForRelativeTobj = [];

            // TODO compress this
            var potentialPaths = LinesToHashSet(Resources.DeepStartPaths);
            ExplorePotentialPaths(potentialPaths);

            var morePaths = FindPathsInUnvisited();
            ExplorePotentialPaths(morePaths);

            FoundDecoyFiles = FindDecoyPaths();
        }

        private void ExplorePotentialPaths(HashSet<string> potentialPaths)
        {
            do
            {
                potentialPaths = FindPaths(potentialPaths);
                potentialPaths.ExceptWith(visited);
            }
            while (potentialPaths.Count > 0);
        }

        /// <summary>
        /// Scans entries which were not visited in the first phase for paths.
        /// </summary>
        /// <returns></returns>
        private HashSet<string> FindPathsInUnvisited()
        {
            HashSet<string> potentialPaths = [];

            var unvisited = reader.Entries.Values.Except(visitedEntries);
            foreach (var entry in unvisited)
            {
                visitedEntries.Add(entry);
                if (entry.IsDirectory)
                {
                    continue;
                }
                if (entry is EntryV2 v2 && v2.TobjMetadata is not null)
                {
                    continue;
                }
                var fileBuffer = reader.Extract(entry, "")[0];

                var type = FileTypeHelper.Infer(fileBuffer);
                try
                {
                    var paths = FindPathsInFile(null, fileBuffer, type);
                    foreach (var path in paths)
                    {
                        Add(path, potentialPaths);
                    }
                }
                catch (Exception ex)
                {
                    #if DEBUG
                        var extension = FileTypeHelper.FileTypeToExtension(type);
                        Console.Error.WriteLine($"Unable to parse {entry.Hash:X16}{extension}: " +
                            $"{ex.GetType().Name}: {ex.Message}");
                    #endif
                    continue;
                }
            }

            return potentialPaths;
        }

        /// <summary>
        /// Finds decoy variants of paths that have been discovered.
        /// </summary>
        /// <returns>The decoy paths.</returns>
        private HashSet<string> FindDecoyPaths()
        {
            HashSet<string> decoyPaths = [];

            foreach (var path in FoundFiles)
            {
                var cleaned = RemoveNonAsciiOrInvalidChars(path);
                if (cleaned != path && reader.FileExists(cleaned))
                {
                    var entry = reader.GetEntry(cleaned);
                    visited.Add(cleaned);
                    visitedEntries.Add(entry);
                    decoyPaths.Add(cleaned);
                }
            }

            return decoyPaths;
        }

        /// <summary>
        /// Searches for potential paths in the given files and directories.
        /// All visited paths are added to <c>this.visited</c>, all visited paths that exist
        /// in the archive are added to <c>this.foundFiles</c>, and all new potential paths
        /// that have been discovered are returned.
        /// </summary>
        /// <param name="inputPaths">The files and directories to search.</param>
        /// <returns>The found potential paths.</returns>
        private HashSet<string> FindPaths(HashSet<string> inputPaths)
        {
            HashSet<string> potentialPaths = [];
            foreach (var path in inputPaths)
            {
                var type = reader.EntryExists(path);
                if (type == EntryType.File)
                {
                    try
                    {
                        potentialPaths.UnionWith(FindPathsInFile(path));
                    }
                    catch (Exception ex)
                    {
                        #if DEBUG
                            Console.Error.WriteLine($"Unable to parse {ReplaceControlChars(path)}: " +
                                $"{ex.GetType().Name}: {ex.Message}");
                        #endif
                    }
                }
                else if (type == EntryType.Directory)
                {
                    potentialPaths.UnionWith(ExploreDirectory(path));
                }
            }
            return potentialPaths;
        }

        /// <summary>
        /// Recursively explores the files and subdirectories of a directory if the archive 
        /// contains a listing for the given path.
        /// </summary>
        /// <param name="dirPath">The directory to explore.</param>
        /// <returns>Paths found in the files of this directory and its subdirectories,
        /// or an empty set if the directory has no listing.</returns>
        private HashSet<string> ExploreDirectory(string dirPath)
        {
            HashSet<string> potentialPaths = [];

            if (!visited.Add(dirPath))
            {
                return [];
            }

            var type = reader.EntryExists(dirPath);
            if (type == EntryType.NotFound)
            {
                Debug.WriteLine($"Directory {ReplaceControlChars(dirPath)} is referenced" +
                    $" in a directory listing but could not be found in the archive");
                return [];
            }
            if (type == EntryType.File)
            {
                var entry = reader.GetEntry(dirPath);
                entry.IsDirectory = true;
            }

            visited.Add(dirPath);
            visitedEntries.Add(reader.GetEntry(dirPath));
            var listing = reader.GetDirectoryListing(dirPath);

            foreach (var filePath in listing.Files)
            {
                potentialPaths.UnionWith(FindPathsInFile(filePath));
            }

            foreach (var subdirPath in listing.Subdirectories)
            {
                potentialPaths.UnionWith(ExploreDirectory(subdirPath));
            }

            return potentialPaths;
        }

        /// <summary>
        /// Returns paths referenced in the given file.
        /// </summary>
        /// <param name="filePath">The path of the file in the archive.</param>
        /// <returns>Paths referenced in the file. If the file does not exist, an empty
        /// set is returned.</returns>
        private HashSet<string> FindPathsInFile(string filePath)
        {
            visited.Add(filePath);
            if (!reader.FileExists(filePath))
            {
                return [];
            }
            FoundFiles.Add(filePath);
            var fileEntry = reader.GetEntry(filePath);
            visitedEntries.Add(fileEntry);
            AddDirToDirsToSearchForRelativeTobj(filePath);

            // skip HashFS v2 tobj/dds entries because we don't need to scan those
            if (fileEntry is EntryV2 v2 && v2.TobjMetadata is not null)
            {
                return [];
            }

            var fileType = FileTypeHelper.PathToFileType(filePath);
            if (IgnorableFileTypes.Contains(fileType))
            {
                return [];
            }

            var fileBuffer = reader.Extract(fileEntry, filePath)[0];
            if (fileType == FileType.Unknown)
            {
                fileType = FileTypeHelper.Infer(fileBuffer);
            }
            return FindPathsInFile(filePath, fileBuffer, fileType);
        }

        /// <summary>
        /// Returns paths referenced in the given file.
        /// </summary>
        /// <param name="filePath">The path of the file in the archive.</param>
        /// <param name="fileBuffer">The extracted content of the file.</param>
        /// <param name="fileType">The file type.</param>
        /// <returns>Paths referenced in the file.</returns>
        private HashSet<string> FindPathsInFile(string filePath, byte[] fileBuffer, FileType fileType)
        {
            if (IgnorableFileTypes.Contains(fileType))
            {
                return [];
            }

            var finders = new Dictionary<FileType, Func<byte[], HashSet<string>>>()
            {
                { FileType.Sii, (fileBuffer) => FindPathsInSii(fileBuffer, filePath) },
                { FileType.SoundRef, FindPathsInSoundref },
                { FileType.Pmd, FindPathsInPmd },
                { FileType.Tobj, FindPathsInTobj },
                { FileType.Material, (fileBuffer) => FindPathsInMat(fileBuffer, filePath) },
                { FileType.Font, FindPathsInFont },
            };

            if (finders.TryGetValue(fileType, out var finder))
            {
                try
                {
                    return finder(fileBuffer);
                }
                catch (Exception ex)
                {
                    #if DEBUG
                        Console.Error.WriteLine($"Unable to parse {filePath} (falling back to FindPathsInBlob): " +
                            $"{ex.GetType().Name}: {ex.Message}");
                    #endif
                    return FindPathsInBlob(fileBuffer);
                }
            }
            else
            {
                return FindPathsInBlob(fileBuffer);
            }
        }

        private HashSet<string> FindPathsInBlob(byte[] fileBuffer)
        {
            HashSet<string> potentialPaths = [];

            int start = 0;
            bool inPotentialPath = false;
            for (int i = 0; i < fileBuffer.Length; i++)
            {
                char c = (char)fileBuffer[i];
                if (inPotentialPath)
                {
                    if (char.IsControl(c) || c == '"' 
                        || i == fileBuffer.Length - 1)
                    {
                        var potentialPath = Encoding.UTF8.GetString(fileBuffer, start, i - start);
                        potentialPaths.Add(potentialPath);
                        inPotentialPath = false;
                    }
                }
                else if (c == '/')
                {
                    start = i;
                    inPotentialPath = true;
                }
            }

            return potentialPaths;
        }

        private HashSet<string> FindPathsInFont(byte[] fileBuffer)
        {
            HashSet<string> potentialPaths = [];

            var matches = Regex.Matches(Encoding.UTF8.GetString(fileBuffer), @"image:(.*?),");
            foreach (Match match in matches)
            {
                potentialPaths.Add(match.Groups[1].Value);
            }

            return potentialPaths;
        }

        private HashSet<string> FindPathsInMat(byte[] fileBuffer, string filePath)
        {
            HashSet<string> potentialPaths = [];

            MatFile mat = MatFile.Load(Encoding.UTF8.GetString(fileBuffer));
            foreach (var texture in mat.Textures)
            {
                if (texture.Attributes.TryGetValue("source", out var value)
                    && value is string path)
                {
                    if (path.StartsWith('/'))
                    {
                        potentialPaths.Add(path);
                    }
                    else
                    {
                        if (filePath == null)
                        {
                            foreach (var dir in dirsToSearchForRelativeTobj)
                            {
                                var potentialAbsPath = $"{dir}/{path}";
                                if (reader.FileExists(potentialAbsPath))
                                {
                                    potentialPaths.Add(potentialAbsPath);
                                }
                            }
                        }
                        else
                        {
                            var matDirectory = GetParent(filePath);
                            path = $"{matDirectory}/{path}";
                            potentialPaths.Add(path);
                        }
                    }
                }
            }

            return potentialPaths;
        }

        private HashSet<string> FindPathsInTobj(byte[] fileBuffer)
        {
            HashSet<string> potentialPaths = [];

            var tobj = Tobj.Load(fileBuffer);
            potentialPaths.UnionWith(tobj.TexturePaths);

            return potentialPaths;
        }

        private HashSet<string> FindPathsInPmd(byte[] fileBuffer)
        {
            HashSet<string> potentialPaths = [];

            var model = Model.Load(fileBuffer, []);
            foreach (var look in model.Looks)
            {
                potentialPaths.UnionWith(look.Materials);
            }

            return potentialPaths;
        }

        private HashSet<string> FindPathsInSoundref(byte[] fileBuffer)
        {
            HashSet<string> potentialPaths = [];

            var lines = Encoding.UTF8.GetString(fileBuffer)
                .Trim(['\uFEFF', '\0'])
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("source="))
                {
                    var path = line[(line.IndexOf('"') + 1)..line.IndexOf('#')];
                    Add(path, potentialPaths);
                }
            }

            return potentialPaths;
        }

        private HashSet<string> FindPathsInSii(byte[] fileBuffer, string filePath)
        {
            var magic = Encoding.UTF8.GetString(fileBuffer[0..4]);
            if (!(magic == "SiiN" // regular sii
                || magic == "\uFEFFS" // regular sii with fucking BOM
                || magic == "ScsC" // encrypted sii
                || magic.StartsWith("3nK") // 3nK-encoded sii
                ))
            {
                #if DEBUG
                    Console.WriteLine($"Not a sii file: {filePath}");
                #endif
                return [];
            }

            var siiDirectory = GetParent(filePath);
            try
            {
                var sii = SiiFile.Load(fileBuffer, siiDirectory, reader, true);
                return FindPathsInSii(sii);
            }
            catch (ParseException)
            {
                if (!filePath.StartsWith("/ui/template"))
                    Debugger.Break();
                throw;
            }
        }

        private HashSet<string> FindPathsInSii(SiiFile sii)
        {
            HashSet<string> potentialPaths = [];

            foreach (var unit in sii.Units)
            {
                foreach (var attrib in unit.Attributes)
                {
                    ProcessSiiUnitAttribute(unit.Class, attrib, potentialPaths);
                }
            }

            return potentialPaths;
        }

        private void ProcessSiiUnitAttribute(string unitClass, KeyValuePair<string, dynamic> attrib,
            HashSet<string> potentialPaths)
        {
            if (attrib.Value is string s)
            {
                if (unitClass == "ui::text" 
                    || unitClass == "ui::text_template" 
                    || unitClass == "ui_text_bar")
                {
                    // Extract paths from img and font tags of the faux-HTML
                    // used in UI strings.
                    var matches = Regex.Matches(s, @"(?:src|face)=(\S*)");
                    foreach (Match match in matches)
                    {
                        Add(match.Groups[1].Value, potentialPaths);
                    }
                }
                else if (attrib.Key == "icon" && unitClass != "mod_package")
                {
                    // See https://modding.scssoft.com/wiki/Documentation/Engine/Units/trailer_configuration
                    // and https://modding.scssoft.com/wiki/Documentation/Engine/Units/accessory_data
                    var iconPath = $"/material/ui/accessory/{attrib.Value}";
                    if (Path.GetExtension(iconPath) == "")
                    {
                        iconPath += ".mat";
                    }
                    Add(iconPath, potentialPaths);
                }
                else
                {
                    Add(s, potentialPaths);
                }
            }
            else if (attrib.Value is List<dynamic> items)
            {
                foreach (var str in items.Where(x => x is string).Cast<string>())
                {
                    if (attrib.Key == "sounds")
                    {
                        var soundPath = str.Split('|')[1];
                        Add(soundPath, potentialPaths);
                    }
                    else
                    {
                        Add(str, potentialPaths);
                    }
                }
            }
        }

        private void Add(string str, HashSet<string> potentialPaths)
        {
            if (potentialPaths.Contains(str) || !IsNewPotentialPath(str))
            {
                return;
            }
            potentialPaths.Add(str);
            AddPathVariants(str, potentialPaths);
        }

        private void AddPathVariants(string str, HashSet<string> potentialPaths)
        {
            var extension = Path.GetExtension(str);
            if (extension == ".pmd")
            {
                Add(Path.ChangeExtension(str, ".pmg"), potentialPaths);
                Add(Path.ChangeExtension(str, ".pmc"), potentialPaths);
                Add(Path.ChangeExtension(str, ".pma"), potentialPaths);
                Add(Path.ChangeExtension(str, ".ppd"), potentialPaths);
            }
            else if (extension == ".pmg")
            {
                Add(Path.ChangeExtension(str, ".pmd"), potentialPaths);
                Add(Path.ChangeExtension(str, ".pmc"), potentialPaths);
                Add(Path.ChangeExtension(str, ".pma"), potentialPaths);
                Add(Path.ChangeExtension(str, ".ppd"), potentialPaths);
            }
            else if (extension == ".bank")
            {
                Add(Path.ChangeExtension(str, ".bank.guids"), potentialPaths);
            }
            else if (extension == ".mat")
            {
                Add(Path.ChangeExtension(str, ".tobj"), potentialPaths);
                Add(Path.ChangeExtension(str, ".dds"), potentialPaths);
            }
            else if (extension == ".tobj")
            {
                Add(Path.ChangeExtension(str, ".mat"), potentialPaths);
                Add(Path.ChangeExtension(str, ".dds"), potentialPaths);
            }
            else if (extension == ".dds")
            {
                Add(Path.ChangeExtension(str, ".mat"), potentialPaths);
                Add(Path.ChangeExtension(str, ".tobj"), potentialPaths);
            }
        }

        private void AddDirToDirsToSearchForRelativeTobj(string filePath)
        {
            foreach (var root in RootsToSearchForRelativeTobj)
            {
                if (filePath.StartsWith(root))
                {
                    dirsToSearchForRelativeTobj.Add(GetParent(filePath));
                }
            }
        }

        private bool IsNewPotentialPath(string str) =>
            !visited.Contains(str)
            && ResemblesPath(str)
            && reader.FileExists(str);
    }
}
