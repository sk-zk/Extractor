using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TruckLib.HashFs;
using static Extractor.PathUtils;
using static Extractor.ConsoleUtils;
using TruckLib.Sii;

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
        /// Overrides the salt found in the header with this one.
        /// </summary>
        /// <remarks>Solves #1.</remarks>
        public ushort? Salt { get; set; } = null;

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

        protected bool PrintExtractedFiles { get; set; } = false;

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
        /// The number of files whose paths had to be changed because they contained
        /// invalid characters.
        /// </summary>
        protected int renamed;

        /// <summary>
        /// The number of files which were skipped because they pointed to
        /// the same offset as another entry.
        /// </summary>
        protected int duplicate;

        /// <summary>
        /// Junk entries identified by DeleteJunkEntries.
        /// </summary>
        protected Dictionary<ulong, IEntry> junkEntries = [];

        private bool hasRemovedJunk;

        public HashFsExtractor(string scsPath, bool overwrite) : base(scsPath, overwrite)
        {
            Reader = HashFsReader.Open(scsPath, ForceEntryTableAtEnd);

            extracted = 0;
            skipped = 0;
            failed = 0;
            notFound = 0;
            renamed = 0;
            duplicate = 0;

            if (Salt is not null)
            {
                Reader.Salt = Salt.Value;
            }
        }

        /// <inheritdoc/>
        public override void Extract(string[] startPaths, string destination)
        {
            DeleteJunkEntries();

            if (startPaths.Length == 1 && startPaths[0] == "/" 
                && Reader.EntryExists("/") == EntryType.Directory)
            {
                var listing = Reader.GetDirectoryListing("/");
                if (listing.Subdirectories.Count == 0 && listing.Files.Count == 0)
                {
                    Console.Error.WriteLine("Top level directory listing is empty; " +
                        "use --deep to scan contents for paths before extraction " +
                        "or --partial to extract known paths");
                    return;
                }
            }

            var scsName = Path.GetFileName(scsPath);

            foreach (var startPath in startPaths)
            {
                Reader.Traverse(startPath,
                    (dir) => PrintExtractionMessage(dir, scsName),
                    (file) =>
                    {
                        if (PrintExtractedFiles)
                        {
                            PrintExtractionMessage(file, scsName);
                        }
                        ExtractFile(file, destination);
                    },
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
                            notFound++;
                        }
                    });
            }
        }

        protected void ExtractFile(string file, string destination)
        {
            // The directory listing of core.scs only lists itself, but as a file, breaking everything
            if (file == "/") return;

            var fileWithoutSlash = RemoveInitialSlash(file);
            var sanitized = SanitizePath(fileWithoutSlash);
            var outputPath = Path.Combine(destination, sanitized);
            if (fileWithoutSlash != sanitized)
            {
                PrintRenameWarning(file, sanitized);
                renamed++;
            }
            ExtractToDisk(file, outputPath, () => ExtractToDiskInner(file, outputPath));
        }

        protected void ExtractToDisk(string displayedPath, string outputPath,
            Action innerCall)
        {
            if (!Overwrite && File.Exists(outputPath))
            {
                skipped++;
                return;
            }

            displayedPath = RemoveInitialSlash(displayedPath);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                innerCall();
                extracted++;
            }
            catch (InvalidDataException idex)
            {
                Console.Error.WriteLine($"Unable to extract {ReplaceControlChars(displayedPath)}:");
                Console.Error.WriteLine(idex.Message);
                failed++;
            }
            catch (AggregateException agex)
            {
                Console.Error.WriteLine($"Unable to extract {ReplaceControlChars(displayedPath)}:");
                Console.Error.WriteLine(agex.ToString());
                failed++;
            }
            catch (IOException ioex)
            {
                Console.Error.WriteLine($"Unable to extract {ReplaceControlChars(displayedPath)}:");
                Console.Error.WriteLine(ioex.Message);
                failed++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unable to extract {ReplaceControlChars(displayedPath)}:");
                Console.Error.WriteLine(ex.ToString());
                failed++;
            }
        }

        private void ExtractToDiskInner(string file, string outputPath, 
            bool allowExtractingJunk = true)
        {
            IEntry entry;
            if (Reader.EntryExists(file) != EntryType.NotFound)
            {
                entry = Reader.GetEntry(file);
            }
            else if (allowExtractingJunk)
            {
                var hash = Reader.HashPath(file);
                if (!junkEntries.TryGetValue(hash, out entry))
                {
                    throw new FileNotFoundException();
                }
            }
            else
            {
                throw new FileNotFoundException();
            }

            if (Path.GetExtension(file) == ".sii")
            {
                var sii = Reader.Extract(entry, file)[0];
                sii = SiiFile.Decode(sii);
                File.WriteAllBytes(outputPath, sii);
            }
            else
            {
                Reader.ExtractToFile(entry, file, outputPath);
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
                Reader.Entries.Remove(hash);
                junkEntries.Add(hash, entry);
                duplicate++;
            }

            hasRemovedJunk = true;
        }

        public override void PrintContentSummary()
        {
            var dirCount = Reader.Entries.Count(x => x.Value.IsDirectory);
            Console.WriteLine($"Opened {Path.GetFileName(scsPath)}: " +
                $"HashFS v{Reader.Version} archive; {Reader.Entries.Count} entries " +
                $"({Reader.Entries.Count - dirCount} files, {dirCount} directory listings)");
        }

        public override void PrintExtractionResult()
        {
            Console.WriteLine($"{extracted} extracted ({renamed} renamed), {skipped} skipped, " +
                $"{notFound} not found, {duplicate} junk, {failed} failed");
            PrintRenameSummary(renamed);
        }

        public override void PrintPaths(string[] startPaths)
        {
            DeleteJunkEntries();

            foreach (var startPath in startPaths)
            {
                Reader.Traverse(startPath,
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

        public override List<Tree.Directory> GetDirectoryTree(string[] startPaths)
        {
            DeleteJunkEntries();

            var trees = startPaths
                .Select(startPath => GetDirectoryTree(startPath))
                .ToList();
            return trees;
        }

        private Tree.Directory GetDirectoryTree(string root)
        {
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
