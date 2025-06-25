using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TruckLib.HashFs;
using static Extractor.PathUtils;
using static Extractor.ConsoleUtils;
using System.Reflection.PortableExecutable;

namespace Extractor
{
    /// <summary>
    /// A HashFS extractor which wraps TruckLib's HashFsReader.
    /// </summary>
    public class HashFsExtractor : Extractor
    {
        /// <summary>
        /// The underlying HashFsReader.
        /// </summary>
        public IHashFsReader Reader { get; private set; }

        /// <summary>
        /// Gets or sets whether the entry table should be read from the end of the file
        /// regardless of where the header says it is located.
        /// </summary>
        /// <remarks>Solves #1.</remarks>
        public bool ForceEntryTableAtEnd { get; set; } = false;

        /// <summary>
        /// Gets or sets whether a "not found" message should be printed
        /// if a start path does not exist in the archive.
        /// </summary>
        public bool PrintNotFoundMessage { get; set; } = true;

        /// <summary>
        /// Junk entries identified by DeleteJunkEntries.
        /// </summary>
        protected Dictionary<ulong, IEntry> junkEntries = [];

        private bool hasRemovedJunk;

        /// <summary>
        /// Paths which will need to be renamed.
        /// </summary>
        protected Dictionary<string, string> substitutions;

        /// <summary>
        /// The number of files which have been extracted successfully.
        /// </summary>
        protected int extracted;

        /// <summary>
        /// The number of files which have been skipped because the output path
        /// already exists and <c>--skip-existing</c> was passed.
        /// </summary>
        protected int skipped;

        /// <summary>
        /// The number of files which failed to extract.
        /// </summary>
        protected int failed;

        /// <summary>
        /// The number of paths passed by the user which were not found in the archive.
        /// </summary>
        protected int notFound;

        /// <summary>
        /// The number of files which were skipped because they pointed to
        /// the same offset as another entry.
        /// </summary>
        protected int duplicate;

        public HashFsExtractor(string scsPath, bool overwrite) : this(scsPath, overwrite, null) 
        { 
        }

        public HashFsExtractor(string scsPath, bool overwrite, ushort? salt) : base(scsPath, overwrite)
        {
            Reader = HashFsReader.Open(scsPath, ForceEntryTableAtEnd);
            PrintContentSummary();

            if (salt is not null)
                Reader.Salt = salt.Value;
            else
                FixSaltIfNecessary();
        }

        /// <inheritdoc/>
        public override void Extract(IList<string> pathFilter, string outputRoot)
        {
            DeleteJunkEntries();

            if (pathFilter.Count == 1 && pathFilter[0] == "/")
            {
                if (Reader.EntryExists("/") == EntryType.Directory)
                {
                    var listing = Reader.GetDirectoryListing("/");
                    if (listing.Subdirectories.Count == 0 && listing.Files.Count == 0)
                    {
                        Console.Error.WriteLine("Top level directory is empty; " +
                            "use --deep to scan contents for paths before extraction " +
                            "or --partial to extract known paths");
                        return;
                    }
                }
                else
                {
                    PrintNoTopLevelError();
                    return;
                }
            }

            var pathsToExtract = GetPathsToExtract(Reader, pathFilter,
                (nonexistent) =>
                {
                    if (PrintNotFoundMessage)
                    {
                        Console.Error.WriteLine($"Path {ReplaceControlChars(nonexistent)} " +
                            $"was not found");
                        notFound++;
                    }
                }
            );
            substitutions = DeterminePathSubstitutions(pathsToExtract);

            ExtractFiles(pathsToExtract, outputRoot);

            WriteRenamedSummary(outputRoot);
            WriteModifiedSummary(outputRoot);
        }

        protected void ExtractFiles(IList<string> pathsToExtract, string outputRoot)
        {
            var scsName = Path.GetFileName(scsPath);
            foreach (var archivePath in pathsToExtract)
            {
                PrintExtractionMessage(archivePath, scsName);
                ExtractFile(archivePath, outputRoot);
            }
        }

        internal static Dictionary<string, string> DeterminePathSubstitutions(IList<string> pathsToExtract)
        {
            Dictionary<string, string> substitutions = [];
            foreach (var path in pathsToExtract)
            {
                var sanitized = SanitizePath(path);
                if (sanitized != path)
                {
                    substitutions.Add(path, sanitized);
                }
            }
            return substitutions;
        }

        internal static List<string> GetPathsToExtract(IHashFsReader reader, IList<string> pathFilter, 
            Action<string> onVisitNonexistent)
        {
            List<string> pathsToExtract = [];
            foreach (var path in pathFilter)
            {
                reader.Traverse(path, 
                    (_) => { }, 
                    (file) => 
                    { 
                        if (file != "/")
                            pathsToExtract.Add(file); 
                    }, 
                    onVisitNonexistent);
            }
            return pathsToExtract;
        }

        protected void ExtractFile(string archivePath, string outputRoot)
        {
            if (!substitutions.TryGetValue(archivePath, out string filePath))
            {
                filePath = archivePath;
            }
            var outputPath = Path.Combine(outputRoot, RemoveInitialSlash(filePath));
            if (archivePath != filePath)
            {
                renamedFiles.Add((archivePath, filePath));
            }

            if (!Overwrite && File.Exists(outputPath))
            {
                skipped++;
                return;
            }

            ExtractToDisk(archivePath, outputPath);
        }

        protected void ExtractToDisk(string archivePath, string outputPath, 
            bool allowExtractingJunk = true)
        {
            var type = Reader.TryGetEntry(archivePath, out var entry);
            if (type == EntryType.NotFound)
            {
                if (allowExtractingJunk)
                {
                    var hash = Reader.HashPath(archivePath);
                    if (!junkEntries.TryGetValue(hash, out entry))
                    {
                        throw new FileNotFoundException();
                    }
                }
                else
                {
                    throw new FileNotFoundException();
                }
            }
            ExtractToDisk(entry, archivePath, outputPath);       
        }

        protected void ExtractToDisk(IEntry entry, string archivePath, string outputPath)
        {
            try
            {
                var buffers = Reader.Extract(entry, archivePath);

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                var wasModified = ExtractWithSubstitutionsIfRequired(archivePath, outputPath, buffers[0], substitutions);

                if (entry is EntryV2 v2 && v2.TobjMetadata is not null)
                {
                    var ddsPath = Path.ChangeExtension(outputPath, "dds");
                    if (Overwrite || !File.Exists(ddsPath))
                    {
                        try
                        {
                            File.WriteAllBytes(ddsPath, buffers[1]);
                        }
                        catch (IOException ioex)
                        {
                            PrintExtractionFailure(Path.ChangeExtension(archivePath, "dds"),
                                ioex.Message);
                        }
                    }
                }

                extracted++;
                if (wasModified)
                    modifiedFiles.Add(archivePath);
            }
            catch (InvalidDataException idex)
            {
                PrintExtractionFailure(archivePath, idex.Message);
            }
            catch (AggregateException agex)
            {
                PrintExtractionFailure(archivePath, agex.ToString());
            }
            catch (IOException ioex)
            {
                PrintExtractionFailure(archivePath, ioex.Message);
            }
            catch (Exception ex)
            {
                PrintExtractionFailure(archivePath, ex.ToString());
            }
        }

        protected void DeleteJunkEntries()
        {
            if (hasRemovedJunk)
                return;

            var visitedOffsets = new Dictionary<ulong, IEntry>();
            var junk = new Dictionary<ulong, IEntry>();
            foreach (var (hash, entry) in Reader.Entries)
            {
                if (!visitedOffsets.TryAdd(entry.Offset, entry))
                {
                    junk.TryAdd(hash, entry);
                    junk.TryAdd(visitedOffsets[entry.Offset].Hash, entry);
                }
            }
            foreach (var (hash, entry) in junk)
            {
                // Reader.Entries.Remove(hash);
                junkEntries.Add(hash, entry);
                duplicate++;
            }

            hasRemovedJunk = true;
        }

        protected void FixSaltIfNecessary()
        {
            const string fileToTest = "manifest.sii";

            if (Reader.TryGetEntry(fileToTest, out var entry) == EntryType.NotFound)
            {
                // No manifest => don't need to run this
                return;
            }

            try
            {
                // Decompresses correctly => no change needed
                Reader.Extract(fileToTest);
                return;
            }
            catch
            {
                // Otherwise, iterate all salts until we find the good one
                Console.Error.WriteLine("Salt may be incorrect; attempting to fix ...");
                for (int i = 0; i < ushort.MaxValue; i++)
                {
                    Reader.Salt = (ushort)i;
                    if (Reader.EntryExists(fileToTest) == EntryType.File)
                    {
                        try
                        {
                            Reader.Extract(fileToTest);
                            Console.Error.WriteLine($"Salt set to {i}");
                            return;
                        }
                        catch { /* Failed to decompress => continue */ }
                    }
                }
                Console.Error.WriteLine("Unable to find true salt");
            }
        }

        public override void PrintContentSummary()
        {
            var dirCount = Reader.Entries.Count(x => x.Value.IsDirectory);
            Console.Error.WriteLine($"Opened {Path.GetFileName(scsPath)}: " +
                $"HashFS v{Reader.Version} archive; {Reader.Entries.Count} entries " +
                $"({Reader.Entries.Count - dirCount} files, {dirCount} directory listings)");
        }

        public override void PrintExtractionResult()
        {
            Console.Error.WriteLine($"{extracted} extracted " +
                $"({renamedFiles.Count} renamed, {modifiedFiles.Count} modified), " +
                $"{skipped} skipped, {notFound} not found, {duplicate} junk, {failed} failed");
            PrintRenameSummary(renamedFiles.Count, modifiedFiles.Count);
        }

        private void PrintExtractionFailure(string archivePath, string errorMessage)
        {
            archivePath = RemoveInitialSlash(archivePath);
            Console.Error.WriteLine($"Unable to extract {ReplaceControlChars(archivePath)}:");
            Console.Error.WriteLine(errorMessage);
            failed++;
        }

        public override void PrintPaths(IList<string> pathFilter, bool includeAll)
        {
            DeleteJunkEntries();

            foreach (var path in pathFilter)
            {
                Reader.Traverse(path,
                    (dir) => Console.WriteLine(ReplaceControlChars(dir)),
                    (file) => Console.WriteLine(ReplaceControlChars(file)),
                    (nonexistent) =>
                    {
                        if (nonexistent == "/")
                        {
                            PrintNoTopLevelError();
                        }
                        else if (PrintNotFoundMessage)
                        {
                            Console.Error.WriteLine($"Path {ReplaceControlChars(nonexistent)} " +
                                $"was not found");
                        }
                    });
            }
        }

        public override List<Tree.Directory> GetDirectoryTree(IList<string> pathFilter)
        {
            DeleteJunkEntries();

            var trees = pathFilter
                .Select(path => GetDirectoryTree(path))
                .ToList();
            return trees;
        }

        private Tree.Directory GetDirectoryTree(string root)
        {
            if (Reader.EntryExists(root) == EntryType.NotFound)
            {
                return null;
            }

            var dir = new Tree.Directory();
            dir.Path = root;
            var entry = Reader.GetEntry(root);
            entry.IsDirectory = true;

            var content = Reader.GetDirectoryListing(root);
            foreach (var subdir in content.Subdirectories)
            {
                var type = Reader.EntryExists(subdir);
                if (type == EntryType.File)
                {
                    var subentry = Reader.GetEntry(root);
                    subentry.IsDirectory = true;
                }
                else if (type == EntryType.NotFound)
                {
                    continue;
                }
                dir.Subdirectories.Add(Path.GetFileName(subdir), GetDirectoryTree(subdir));
            }
            foreach (var file in content.Files)
            {
                if (!junkEntries.ContainsKey(Reader.HashPath(file)))
                {
                    dir.Files.Add(file);
                }
            }
            return dir;
        }

        public override void Dispose()
        {
            Reader.Dispose();
        }
    }
}
