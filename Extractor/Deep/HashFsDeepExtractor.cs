using Sprache;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Extractor.PathUtils;
using static Extractor.ConsoleUtils;
using TruckLib.Sii;
using System.Data;
using TruckLib.HashFs;

namespace Extractor.Deep
{
    /// <summary>
    /// A HashFS extractor which scans entry contents for paths before extraction to simplify
    /// the extraction of archives which lack dictory listings.
    /// </summary>
    public class HashFsDeepExtractor : HashFsExtractor
    {
        /// <summary>
        /// If true, perform path search in a single thread.
        /// </summary>
        public bool SingleThreadedPathSearch { get; set; } = false;
        /// <summary>
        /// Number of parallel readers to use for deep search. Default: logical CPU count.
        /// </summary>
        public int ReaderPoolSize { get; set; } = Math.Max(1, Environment.ProcessorCount);
        /// <summary>
        /// Additional start paths which the user specified with the <c>--additional</c> parameter.
        /// </summary>
        public IList<string> AdditionalStartPaths { get; set; } = [];

        /// <summary>
        /// The directory to which files whose paths were not discovered
        /// will be written.
        /// </summary>
        private const string DumpDirectory = "_unknown";

        /// <summary>
        /// The directory to which decoy files will be written.
        /// </summary>
        private const string DecoyDirectory = "_decoy";

        /// <summary>
        /// The number of files whose paths were not discovered and therefore have been
        /// dumped to <see cref="DumpDirectory"/>.
        /// </summary>
        private int dumped;

        private HashFsPathFinder finder;

        private bool hasSearchedForPaths;

        public HashFsDeepExtractor(string scsPath, bool overwrite, ushort? salt) 
            : base(scsPath, overwrite, salt)
        {
            IdentifyJunkEntries();
        }

        /// <inheritdoc/>
        public override void Extract(IList<string> pathFilter, string outputRoot)
        {
            Console.WriteLine("Searching for paths ...");
            FindPaths();
            var foundFiles = finder.FoundFiles.Order().ToArray();
            Extract(foundFiles, pathFilter, outputRoot, false);
        }

        public void Extract(string[] foundFiles, IList<string> pathFilter, string outputRoot, bool ignoreMissing)
        {
            bool filtersSet = !pathFilter.SequenceEqual(["/"]);

            substitutions = DeterminePathSubstitutions(foundFiles);

            if (filtersSet)
            {
                foundFiles = foundFiles
                    .Where(f => pathFilter.Any(f.StartsWith)).ToArray();
            }
            ExtractFiles(foundFiles, outputRoot, ignoreMissing);

            var foundDecoyFiles = finder.FoundDecoyFiles.Order().ToArray();
            if (filtersSet)
            {
                foundDecoyFiles = foundDecoyFiles
                    .Where(f => pathFilter.Any(f.StartsWith)).ToArray();
            }
            var decoyDestination = Path.Combine(outputRoot, DecoyDirectory);
            foreach (var decoyFile in foundDecoyFiles)
            {
                ExtractFile(decoyFile, decoyDestination);
            }

            if (!filtersSet)
            {
                DumpUnrecovered(outputRoot, foundFiles.Concat(foundDecoyFiles));
            }

            WriteRenamedSummary(outputRoot);
            WriteModifiedSummary(outputRoot);
        }

        public (HashSet<string> FoundFiles, HashSet<string> ReferencedFiles) FindPaths()
        {
            if (!hasSearchedForPaths)
            {
                finder = new HashFsPathFinder(Reader, AdditionalStartPaths, junkEntries,
                    SingleThreadedPathSearch, CreateReaderClone, ReaderPoolSize);
                finder.Find();
                // Populate detailed timing metrics
                SearchExtractTime = finder.ExtractTime;
                SearchParseTime = finder.ParseTime;
                SearchFilesParsed = finder.FilesParsed;
                SearchBytesInflated = finder.BytesInflated;
                SearchExtractWallTime = finder.ExtractWallTime;
                // Report unique as the number of unique discovered files (stable across ST/MT)
                SearchUniqueFilesParsed = finder.FoundFiles?.Count;
                SearchTime = (finder.ExtractTime + finder.ParseTime);
                hasSearchedForPaths = true;
            }
            return (finder.FoundFiles, finder.ReferencedFiles);
        }

        /// <summary>
        /// Extracts files whose paths were not discovered.
        /// </summary>
        /// <param name="destination">The root output directory.</param>
        /// <param name="foundFiles">All discovered paths.</param>
        private void DumpUnrecovered(string destination, IEnumerable<string> foundFiles)
        {
            if (DryRun)
                return;

            var notRecovered = Reader.Entries.Values
                .Where(e => !e.IsDirectory)
                .Except(foundFiles.Select(f =>
                {
                    if (Reader.EntryExists(f) != EntryType.NotFound)
                    {
                        return Reader.GetEntry(f);
                    }
                    junkEntries.TryGetValue(Reader.HashPath(f), out var retval);
                    return retval;
                }));

            HashSet<ulong> visitedOffsets = [];

            var outputDir = Path.Combine(destination, DumpDirectory);

            foreach (var entry in notRecovered)
            {
                if (junkEntries.ContainsKey(entry.Hash))
                    continue;

                if (maybeJunkEntries.ContainsKey(entry.Hash))
                    continue;

                var fileBuffer = Reader.Extract(entry, "")[0];
                var fileType = FileTypeHelper.Infer(fileBuffer);
                var extension = FileTypeHelper.FileTypeToExtension(fileType);
                var fileName = entry.Hash.ToString("x16") + extension;
                var outputPath = Path.Combine(outputDir, fileName);
                Console.WriteLine($"Dumping {fileName} ...");
                if (!Overwrite && File.Exists(outputPath))
                {
                    skipped++;
                }
                else
                {
                    ExtractToDisk(entry, $"/{DumpDirectory}/{fileName}", outputPath);
                    dumped++;
                }
            }
        }

        public override void PrintPaths(IList<string> pathFilter, bool includeAll)
        {
            var finder = new HashFsPathFinder(Reader,
                singleThreaded: SingleThreadedPathSearch,
                readerFactory: CreateReaderClone,
                readerPoolSize: ReaderPoolSize);
            finder.Find();
            var paths = (includeAll
                ? finder.FoundFiles.Union(finder.ReferencedFiles)
                : finder.FoundFiles).Order();

            foreach (var path in paths)
            {
                Console.WriteLine(ReplaceControlChars(path));
            }
        }

        public override void PrintExtractionResult()
        {
            var times = (OpenTime.HasValue || SearchTime.HasValue || ExtractTime.HasValue)
                ? $" | open={OpenTime?.TotalMilliseconds:F0}ms, " +
                  $"search={SearchTime?.TotalMilliseconds:F0}ms" +
                  (SearchExtractTime.HasValue || SearchParseTime.HasValue || SearchFilesParsed.HasValue || SearchBytesInflated.HasValue
                    ? $" (decomp={SearchExtractTime?.TotalMilliseconds:F0}ms, decomp_wall={SearchExtractWallTime?.TotalMilliseconds:F0}ms, parse={SearchParseTime?.TotalMilliseconds:F0}ms, files={SearchFilesParsed?.ToString() ?? "-"}, unique={SearchUniqueFilesParsed?.ToString() ?? "-"}, bytes={SearchBytesInflated?.ToString() ?? "-"})"
                    : string.Empty)
                  + $", extract={ExtractTime?.TotalMilliseconds:F0}ms"
                : string.Empty;
            Console.WriteLine($"{extracted} extracted " +
                $"({renamedFiles.Count} renamed, {modifiedFiles.Count} modified, {dumped} dumped), " +
                $"{skipped} skipped, {duplicate} junk, {failed} failed" + times);
            PrintRenameSummary(renamedFiles.Count, modifiedFiles.Count);
        }

        public override List<Tree.Directory> GetDirectoryTree(IList<string> pathFilter)
        {
            var finder = new HashFsPathFinder(Reader,
                singleThreaded: SingleThreadedPathSearch,
                readerFactory: CreateReaderClone,
                readerPoolSize: ReaderPoolSize);
            finder.Find();

            var trees = pathFilter
                .Select(startPath => PathListToTree(startPath, finder.FoundFiles))
                .ToList();
            return trees;
        }

        private TruckLib.HashFs.IHashFsReader CreateReaderClone()
        {
            var r = TruckLib.HashFs.HashFsReader.Open(ScsPath, ForceEntryTableAtEnd);
            r.Salt = Reader.Salt;
            return r;
        }
    }
}

