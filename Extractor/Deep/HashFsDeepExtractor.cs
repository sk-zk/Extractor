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

namespace Extractor.Deep
{
    /// <summary>
    /// A HashFS extractor which scans entry contents for paths before extraction to simplify
    /// the extraction of archives which lack dictory listings.
    /// </summary>
    public class HashFsDeepExtractor : HashFsExtractor
    {
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

        /// <summary>
        /// The number of undiscovered files which were skipped because they pointed to
        /// the same offset as another entry.
        /// </summary>
        private int duplicate;

        public HashFsDeepExtractor(string scsPath, bool overwrite) : base(scsPath, overwrite)
        {
            dumped = 0;
            duplicate = 0;
            PrintExtractedFiles = true;
        }

        public override void Extract(string[] startPaths, string destination)
        {
            Console.WriteLine("Searching for paths ...");

            var finder = new PathFinder(Reader);
            finder.Find();

            bool startPathsSet = !startPaths.SequenceEqual(["/"]);

            var foundFiles = finder.FoundFiles.Order().ToArray();
            if (startPathsSet)
            {
                foundFiles = foundFiles
                    .Where(f => startPaths.Any(f.StartsWith)).ToArray();
            }
            base.Extract(foundFiles, destination);

            var foundDecoyFiles = finder.FoundDecoyFiles.Order().ToArray();
            if (startPathsSet)
            {
                foundDecoyFiles = foundDecoyFiles
                    .Where(f => startPaths.Any(f.StartsWith)).ToArray();
            }
            base.Extract(foundDecoyFiles, Path.Combine(destination, DecoyDirectory));

            if (!startPathsSet)
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
                            .Except(foundFiles.Select(Reader.GetEntry));

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
                    ExtractToFile(fileName, outputPath, () =>
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

        public override void PrintPaths(string[] startPaths)
        {
            var finder = new PathFinder(Reader);
            finder.Find();
            var foundFiles = finder.FoundFiles.Order().ToArray();

            foreach (var foundFile in foundFiles)
            {
                Console.WriteLine(ReplaceControlChars(foundFile));
            }
        }

        public override void PrintExtractionResult()
        {
            Console.WriteLine($"{extracted} extracted ({renamed} renamed, {dumped} dumped), " +
                $"{skipped} skipped, {duplicate} junk, {failed} failed");
            PrintRenameSummary(renamed);
        }

        public override List<Tree.Directory> GetDirectoryTree(string[] startPaths)
        {
            var finder = new PathFinder(Reader);
            finder.Find();

            var trees = startPaths
                .Select(startPath => PathListToTree(startPath, finder.FoundFiles))
                .ToList();
            return trees;
        }
    }
}
