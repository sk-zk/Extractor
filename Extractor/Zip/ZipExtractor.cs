using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Extractor.PathUtils;
using static Extractor.ConsoleUtils;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Extractor.Tests")]
namespace Extractor.Zip
{
    /// <summary>
    /// A ZIP extractor which can handle archives with deliberately corrupted local file headers.
    /// </summary>
    public class ZipExtractor : Extractor
    {
        /// <summary>
        /// The underlying ZIP reader.
        /// </summary>
        private readonly ZipReader zip;

        /// <summary>
        /// Paths which will need to be renamed.
        /// </summary>
        private Dictionary<string, string> substitutions;

        /// <summary>
        /// The number of files which have been extracted successfully.
        /// </summary>
        private int extracted;

        /// <summary>
        /// The number of files which have been skipped because the output path
        /// already exists and <c>--skip-existing</c> was passed.
        /// </summary>
        private int skipped;

        /// <summary>
        /// The number of files which failed to extract.
        /// </summary>
        private int failed;

        public ZipExtractor(string scsPath, bool overwrite) 
            : base(scsPath, overwrite)
        {
            zip = ZipReader.Open(scsPath);
        }

        /// <inheritdoc/>
        public override void Extract(IList<string> pathFilter, string outputRoot)
        {
            var entriesToExtract = GetEntriesToExtract(zip, pathFilter);
            substitutions = DeterminePathSubstitutions(entriesToExtract);

            var scsName = Path.GetFileName(scsPath);
            foreach (var entry in entriesToExtract)
            {
                try
                {
                    PrintExtractionMessage(entry.FileName, scsName);
                    if (!substitutions.TryGetValue(entry.FileName, out string fileName))
                    {
                        fileName = entry.FileName;
                    }
                    ExtractEntry(entry, outputRoot, fileName);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Unable to extract {ReplaceControlChars(entry.FileName)}:");
                    Console.Error.WriteLine(ex.Message);
                    failed++;
                }
            }

            WriteRenamedSummary(outputRoot);
            WriteModifiedSummary(outputRoot);
        }

        internal static Dictionary<string, string> DeterminePathSubstitutions(
            List<CentralDirectoryFileHeader> entriesToExtract)
        {
            Dictionary<string, string> substitutions = [];
            foreach (var entry in entriesToExtract)
            {
                var sanitized = SanitizePath(entry.FileName);
                if (sanitized != entry.FileName)
                {
                    substitutions.Add(entry.FileName, sanitized);
                    substitutions.Add("/" + entry.FileName, "/" + sanitized);
                }
            }
            return substitutions;
        }

        internal static List<CentralDirectoryFileHeader> GetEntriesToExtract(
            ZipReader zip, IList<string> pathFilter)
        {
            bool pathFilterUsed = !pathFilter.SequenceEqual(["/"]);
            if (pathFilterUsed)
                RemoveInitialSlash(pathFilter);

            List<CentralDirectoryFileHeader> entriesToExtract = [];
            foreach (var entry in zip.Entries)
            {
                // Directory metadata; ignore
                if (entry.FileName.EndsWith('/'))
                    continue;

                if (pathFilterUsed && !pathFilter.Any(entry.FileName.StartsWith))
                    continue;

                entriesToExtract.Add(entry);
            }

            return entriesToExtract;
        }

        /// <summary>
        /// Extracts the given file from the archive to the destination directory.
        /// </summary>
        /// <param name="entry">The file to extract.</param>
        /// <param name="destination">The directory to extract the file to.</param>
        private void ExtractEntry(CentralDirectoryFileHeader entry, string outputRoot, string fileName)
        {
            var outputPath = Path.Combine(outputRoot, fileName);
            if (File.Exists(outputPath) && !Overwrite)
            {
                skipped++;
                return;
            }

            if (fileName != entry.FileName)
            {
                renamedFiles.Add((entry.FileName, fileName));
            }

            var outputDir = Path.GetDirectoryName(outputPath);
            if (outputDir != null)
            {
                Directory.CreateDirectory(outputDir);
            }

            using var ms = new MemoryStream();
            zip.GetEntry(entry, ms);
            var wasModified = ExtractWithSubstitutionsIfRequired(entry.FileName, outputPath, 
                ms.ToArray(), substitutions);

            extracted++;
            if (wasModified)
                modifiedFiles.Add(entry.FileName);
        }

        public override void PrintContentSummary()
        {
            Console.Error.WriteLine($"Opened {Path.GetFileName(scsPath)}: " +
                $"ZIP archive; {zip.Entries.Count} entries");
        }

        public override void PrintExtractionResult()
        {
            Console.Error.WriteLine($"{extracted} extracted " +
                $"({renamedFiles.Count} renamed, {modifiedFiles.Count} modified), " +
                $"{skipped} skipped, {failed} failed");
            PrintRenameSummary(renamedFiles.Count, modifiedFiles.Count);
        }

        public override void PrintPaths(IList<string> pathFilter, bool includeAll)
        {
            foreach (var entry in zip.Entries)
            {
                Console.WriteLine(ReplaceControlChars(entry.FileName));
            }
        }

        public override List<Tree.Directory> GetDirectoryTree(IList<string> pathFilter)
        {
            RemoveInitialSlash(pathFilter);

            var paths = zip.Entries
                .Where(e => e.UncompressedSize > 0) // filter out directory metadata
                .Select(e => e.FileName);
            var trees = pathFilter
                .Select(startPath => PathListToTree(startPath, paths))
                .ToList();
            return trees;
        }

        private static void RemoveInitialSlash(IList<string> pathFilter)
        {
            for (int i = 0; i < pathFilter.Count; i++)
            {
                if (pathFilter[i] != "/" && pathFilter[i].StartsWith('/'))
                {
                    pathFilter[i] = pathFilter[i][1..];
                }
            }
        }
        public override void Dispose()
        {
            zip?.Dispose();
        }
    }
}
