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
using System.Reflection.PortableExecutable;
using TruckLib.HashFs;

namespace Extractor.Deep
{
    /// <summary>
    /// A HashFS extractor which scans entry contents for paths before extraction to simplify
    /// the extraction of archives which lack dictory listings.
    /// </summary>
    public class HashFsDeepExtractor : HashFsExtractor
    {
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

        public HashFsDeepExtractor(string scsPath, bool overwrite) : base(scsPath, overwrite)
        {
            dumped = 0;
            PrintExtractedFiles = true;
        }

        public override void Extract(IList<string> pathFilter, string destination)
        {
            Console.WriteLine("Searching for paths ...");

            DeleteJunkEntries();

            var finder = new PathFinder(Reader, AdditionalStartPaths, junkEntries);
            finder.Find();

            bool filtersSet = !pathFilter.SequenceEqual(["/"]);

            var foundFiles = finder.FoundFiles.Order().ToArray();
            if (filtersSet)
            {
                foundFiles = foundFiles
                    .Where(f => pathFilter.Any(f.StartsWith)).ToArray();
            }
            base.Extract(foundFiles, destination);

            var foundDecoyFiles = finder.FoundDecoyFiles.Order().ToArray();
            if (filtersSet)
            {
                foundDecoyFiles = foundDecoyFiles
                    .Where(f => pathFilter.Any(f.StartsWith)).ToArray();
            }
            var decoyDestination = Path.Combine(destination, DecoyDirectory);
            foreach (var decoyFile in foundDecoyFiles)
            {
                base.ExtractFile(decoyFile, decoyDestination);
            }

            if (!filtersSet)
            {
                DumpUnrecovered(destination, foundFiles.Concat(foundDecoyFiles));
            }
        }

        /// <summary>
        /// Extracts files whose paths were not discovered.
        /// </summary>
        /// <param name="destination">The root output directory.</param>
        /// <param name="foundFiles">All discovered paths.</param>
        private void DumpUnrecovered(string destination, IEnumerable<string> foundFiles)
        {
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
                if (!visitedOffsets.Add(entry.Offset))
                {
                    duplicate++;
                    continue;
                }

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
                    ExtractToDisk(fileName, outputPath, () =>
                    {
                        if (fileType == FileType.Sii)
                        {
                            fileBuffer = SiiFile.Decode(fileBuffer);
                        }
                        File.WriteAllBytes(outputPath, fileBuffer);
                    });
                    dumped++;
                }
            }
        }

        public override void PrintPaths(IList<string> pathFilter, bool includeAll)
        {
            DeleteJunkEntries();

            var finder = new PathFinder(Reader);
            finder.Find();
            var paths = (includeAll 
                ? finder.FoundFiles.Union(finder.ReferencedFiles) 
                : finder.FoundFiles).Order().ToArray();

            foreach (var path in paths)
            {
                Console.WriteLine(ReplaceControlChars(path));
            }
        }

        public override void PrintExtractionResult()
        {
            Console.WriteLine($"{extracted} extracted ({renamed} renamed, {dumped} dumped), " +
                $"{skipped} skipped, {duplicate} junk, {failed} failed");
            PrintRenameSummary(renamed);
        }

        public override List<Tree.Directory> GetDirectoryTree(IList<string> pathFilter)
        {
            DeleteJunkEntries();

            var finder = new PathFinder(Reader);
            finder.Find();

            var trees = pathFilter
                .Select(startPath => PathListToTree(startPath, finder.FoundFiles))
                .ToList();
            return trees;
        }
    }
}
