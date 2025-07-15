using Extractor.Properties;
using GisDeflate;
using Sprache;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
        /// File paths that are referenced by files in this archive.
        /// </summary>
        public HashSet<string> ReferencedFiles { get; private set; }

        /// <summary>
        /// File types which can be ignored because they don't contain
        /// any path references.
        /// </summary>
        private static readonly FileType[] IgnorableFileTypes = [
            FileType.Bmp,
            FileType.Dds,
            FileType.Fbx,
            FileType.GimpXcf,
            FileType.Ini,
            FileType.Jpeg,
            FileType.MapRoot,
            FileType.MapBase,
            FileType.MapAux,
            FileType.MapSnd,
            FileType.MapData,
            FileType.MapDesc,
            FileType.MapLayer,
            FileType.MapSelection,
            FileType.MayaAsciiScene,
            FileType.MayaBinaryScene,
            FileType.MayaFile,
            FileType.MayaSwatch,
            FileType.MayaSwatches,
            FileType.Ogg,
            FileType.OpenExr,
            FileType.Pma,
            FileType.Pmc,
            FileType.Pmg,
            FileType.Png,
            FileType.Ppd,
            FileType.Psd,
            FileType.Rar,
            FileType.Replay,
            FileType.Sct,
            FileType.SoundBank,
            FileType.SoundBankGuids,
            FileType.SubstanceDesignerSource,
            FileType.Sui, // handled by the including .sii files
            FileType.Svg,
            FileType.TgaMask,
            FileType.WxWidgetsResource,
            FileType.ZlibBlob,
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

        private IList<string> additionalStartPaths;

        /// <summary>
        /// Paths that have been visited so far.
        /// </summary>
        private HashSet<string> visited;

        /// <summary>
        /// Entries that have been visited so far.
        /// </summary>
        private HashSet<IEntry> visitedEntries;

        /// <summary>
        /// Junk entries identified by DeleteJunkEntries.
        /// </summary>
        private Dictionary<ulong, IEntry> junkEntries;

        /// <summary>
        /// Directories discovered during the first phase which might contain tobj files.
        /// This is used to find the absolute path of tobj files that are referenced in 
        /// mat files for which the mat path was not found and the tobj is referenced as
        /// relative path only.
        /// </summary>
        private HashSet<string> dirsToSearchForRelativeTobj;

        public PathFinder(IHashFsReader reader, IList<string> additionalStartPaths = null, 
            Dictionary<ulong, IEntry> junkEntries = null)
        {
            this.reader = reader;
            this.additionalStartPaths = additionalStartPaths;
            this.junkEntries = junkEntries ?? [];
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
            ReferencedFiles = [];

            var potentialPaths = LoadStartPaths();
            if (additionalStartPaths is not null)
                potentialPaths.UnionWith(additionalStartPaths);
            ExplorePotentialPaths(potentialPaths);

            var morePaths = FindPathsInUnvisited();
            ExplorePotentialPaths(morePaths);

            FoundDecoyFiles = FindDecoyPaths();
        }

        private static HashSet<string> LoadStartPaths()
        {
            using var inMs = new MemoryStream(Resources.DeepStartPaths);
            using var ds = new DeflateStream(inMs, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            ds.CopyTo(outMs);
            var lines = Encoding.UTF8.GetString(outMs.ToArray());
            return LinesToHashSet(lines);
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
            HashSet<ulong> visitedOffsets = [];
            HashSet<string> potentialPaths = [];

            var unvisited = reader.Entries.Values.Except(visitedEntries);
            foreach (var entry in unvisited)
            {
                visitedEntries.Add(entry);
                // Some archives contain a large number of garbage entries all pointing
                // to the same offset and claiming to be many MB in size.
                // Let's not waste our time on those.
                if (!visitedOffsets.Add(entry.Offset))
                {
                    continue;
                }

                if (entry.IsDirectory)
                {
                    continue;
                }
                if (entry is EntryV2 v2 && v2.TobjMetadata is not null)
                {
                    continue;
                }

                byte[] fileBuffer;
                try
                {
                    fileBuffer = reader.Extract(entry, "")[0];
                }
                catch (InvalidDataException)
                {
                    #if DEBUG
                        Console.WriteLine($"Unable to decompress, likely junk: {entry.Hash:X16}");
                    #endif
                    junkEntries.TryAdd(entry.Hash, entry);
                    continue;
                }
                catch (Exception)
                {
                    #if DEBUG
                        Debugger.Break();
                    #endif
                    continue;
                }

                var type = FileTypeHelper.Infer(fileBuffer);
                if (type == FileType.Material)
                {
                    // The file path of automats is derived from the CityHash64 of its content,
                    // so we add this automat path to the set of paths to check
                    var contentHash = CityHash.CityHash64(fileBuffer, (ulong)fileBuffer.Length);
                    var contentHashStr = contentHash.ToString("x16");
                    var automatPath = $"/automat/{contentHashStr[..2]}/{contentHashStr}.mat";
                    potentialPaths.Add(automatPath);
                }
                try
                {
                    var paths = FindPathsInFile(null, fileBuffer, type);
                    foreach (var path in paths)
                    {
                        Add(path, potentialPaths, visited);
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
                if (cleaned != path)
                {
                    if (junkEntries.ContainsKey(reader.HashPath(cleaned)))
                    {
                        decoyPaths.Add(cleaned);
                    }
                    else if (reader.FileExists(cleaned))
                    {
                        var entry = reader.GetEntry(cleaned);
                        visited.Add(cleaned);
                        visitedEntries.Add(entry);
                        decoyPaths.Add(cleaned);
                    }
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
                if (visited.Contains(path))
                    continue;

                reader.Traverse(path,
                    (dir) => 
                    {
                        var isNew = visited.Add(dir);
                        if (isNew)
                        {
                            visitedEntries.Add(reader.GetEntry(dir));
                        }
                        return isNew;
                    },
                    (file) =>
                    {
                        try
                        {
                            var paths = FindPathsInFile(file);
                            potentialPaths.UnionWith(paths);
                            ReferencedFiles.UnionWith(paths);
                        }
                        catch (Exception ex)
                        {
                            #if DEBUG
                            Console.Error.WriteLine($"Unable to parse {ReplaceControlChars(file)}: " +
                                $"{ex.GetType().Name}: {ex.Message.Trim()}");
                            #endif
                        }
                    },
                    (_) => { }
                    );
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

            if (junkEntries.ContainsKey(reader.HashPath(filePath)))
            {
                return [];
            }

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
                { FileType.Sii, (fileBuffer) =>
                    {
                        var potentialPaths = SiiPathFinder.FindPathsInSii(fileBuffer, filePath, reader);
                        potentialPaths.ExceptWith(visited);
                        return potentialPaths;
                    } },
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
                            $"{ex.GetType().Name}: {ex.Message.Trim()}");
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
                        Add(potentialPath, potentialPaths, visited);
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
                var path = match.Groups[1].Value;
                Add(path, potentialPaths, visited);
                if (path.EndsWith(".mat"))
                {
                    Add(Path.ChangeExtension(path, ".font"), potentialPaths, visited);
                }
            }

            return potentialPaths;
        }

        private HashSet<string> FindPathsInMat(byte[] fileBuffer, string filePath)
        {
            HashSet<string> potentialPaths = [];

            MatFile mat;
            try
            {
                mat = MatFile.Load(Encoding.UTF8.GetString(fileBuffer));
            }
            catch (Exception)
            {
                Debugger.Break();
                throw;
            }

            foreach (var texture in mat.Textures)
            {
                if (texture.Attributes.TryGetValue("source", out var value)
                    && value is string path)
                {
                    if (path.StartsWith('/'))
                    {
                        Add(path, potentialPaths, visited);
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
                                    Add(potentialAbsPath, potentialPaths, visited);
                                }
                            }
                        }
                        else
                        {
                            var matDirectory = GetParent(filePath);
                            path = $"{matDirectory}/{path}";
                            Add(path, potentialPaths, visited);
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
            potentialPaths.Add(tobj.TexturePath);
            ReferencedFiles.Add(tobj.TexturePath);

            return potentialPaths;
        }

        private HashSet<string> FindPathsInPmd(byte[] fileBuffer)
        {
            HashSet<string> potentialPaths = [];

            var model = Model.Load(fileBuffer, []);
            foreach (var look in model.Looks)
            {
                potentialPaths.UnionWith(look.Materials);
                ReferencedFiles.UnionWith(look.Materials);
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
                    Add(path, potentialPaths, visited);
                }
            }

            return potentialPaths;
        }

        internal static void Add(string str, HashSet<string> potentialPaths, HashSet<string> visited)
        {
            if (!ResemblesPath(str))
            {
                return;
            }

            if (!str.StartsWith('/'))
            {
                str = $"/{str}";
            }

            if (!potentialPaths.Contains(str) && !visited.Contains(str))
            {
                potentialPaths.Add(str);
                AddPathVariants(str, potentialPaths, visited);
            }
        }

        private static void AddPathVariants(string str, HashSet<string> potentialPaths, HashSet<string> visited)
        {
            var extension = Path.GetExtension(str);

            if (extension == ".pmd")
            {
                Add(Path.ChangeExtension(str, ".pmg"), potentialPaths, visited);
                Add(Path.ChangeExtension(str, ".pmc"), potentialPaths, visited);
                Add(Path.ChangeExtension(str, ".pma"), potentialPaths, visited);
                Add(Path.ChangeExtension(str, ".ppd"), potentialPaths, visited);
            }
            else if (extension == ".pmg")
            {
                Add(Path.ChangeExtension(str, ".pmd"), potentialPaths, visited);
                Add(Path.ChangeExtension(str, ".pmc"), potentialPaths, visited);
                Add(Path.ChangeExtension(str, ".pma"), potentialPaths, visited);
                Add(Path.ChangeExtension(str, ".ppd"), potentialPaths, visited);
            }
            else if (extension == ".bank")
            {
                Add(Path.ChangeExtension(str, ".bank.guids"), potentialPaths, visited);
            }
            else if (extension == ".mat")
            {
                Add(Path.ChangeExtension(str, ".tobj"), potentialPaths, visited);
                Add(Path.ChangeExtension(str, ".dds"), potentialPaths, visited);
            }
            else if (extension == ".tobj")
            {
                Add(Path.ChangeExtension(str, ".mat"), potentialPaths, visited);
                Add(Path.ChangeExtension(str, ".dds"), potentialPaths, visited);
            }
            else if (extension == ".dds")
            {
                Add(Path.ChangeExtension(str, ".mat"), potentialPaths, visited);
                Add(Path.ChangeExtension(str, ".tobj"), potentialPaths, visited);
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
    }
}
