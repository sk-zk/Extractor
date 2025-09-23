using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TruckLib;
using TruckLib.Models;
using TruckLib.Sii;
using static Extractor.PathSubstitution;

namespace Extractor
{
    /// <summary>
    /// Base class for archive extractors.
    /// </summary>
    public abstract class Extractor : IDisposable
    {
        public abstract IFileSystem FileSystem { get; }

        /// <summary>
        /// Gets or sets whether existing files should be overwritten.
        /// </summary>
        public bool Overwrite { get; set; } = true;

        /// <summary>
        /// If true, skip writing files to disk during extraction.
        /// </summary>
        public bool DryRun { get; set; } = false;

        /// <summary>
        /// The absolute path of the archive file.
        /// </summary>
        public string ScsPath { get; init; }

        /// <summary>
        /// Timing: time spent opening the archive.
        /// </summary>
        public TimeSpan? OpenTime { get; set; }

        /// <summary>
        /// Timing: time spent searching for paths (when applicable).
        /// </summary>
        public TimeSpan? SearchTime { get; set; }

        /// <summary>
        /// Timing: time spent extracting files.
        /// </summary>
        public TimeSpan? ExtractTime { get; set; }

        /// <summary>
        /// Timing detail: time spent in search while decompressing/reading entries.
        /// </summary>
        public TimeSpan? SearchExtractTime { get; set; }

        /// <summary>
        /// Timing detail: time spent in search while parsing entries.
        /// </summary>
        public TimeSpan? SearchParseTime { get; set; }

        /// <summary>
        /// Detail: number of files parsed during search.
        /// </summary>
        public int? SearchFilesParsed { get; set; }

        /// <summary>
        /// Detail: total decompressed bytes read during search.
        /// </summary>
        public long? SearchBytesInflated { get; set; }

        /// <summary>
        /// Detail: wall-clock time window spent performing IO/decompression during search.
        /// </summary>
        public TimeSpan? SearchExtractWallTime { get; set; }

        /// <summary>
        /// Detail: unique files parsed during search.
        /// </summary>
        public int? SearchUniqueFilesParsed { get; set; }

        /// <summary>
        /// Files which were renamed because they contained invalid characters.
        /// </summary>
        protected List<(string ArchivePath, string SanitizedPath)> renamedFiles = [];

        /// <summary>
        /// Files which were modified to replace references to paths which had 
        /// to be renamed.
        /// </summary>
        protected List<string> modifiedFiles = [];

        public Extractor(string scsPath, bool overwrite) 
        { 
            ScsPath = scsPath;
            Overwrite = overwrite; 
        }

        /// <summary>
        /// Extracts the archive to the given directory.
        /// </summary>
        /// <param name="pathFilter">A list of paths to extract. Pass ["/"] to extract the entire archive.</param>
        /// <param name="destination">The directory to which the extracted files will be written.</param>
        public abstract void Extract(IList<string> pathFilter, string destination);

        public abstract List<Tree.Directory> GetDirectoryTree(IList<string> pathFilter);

        public abstract void PrintPaths(IList<string> pathFilter, bool includeAll);

        public abstract void PrintContentSummary();

        public abstract void PrintExtractionResult();

        public abstract void Dispose();

        internal static bool ExtractWithSubstitutionsIfRequired(string archivePath, string outputPath,
            byte[] buffer, Dictionary<string, string> substitutions)
        {
            var wasModified = false;

            var extension = Path.GetExtension(archivePath).ToLowerInvariant();
            if (substitutions is not null && substitutions.Count > 0)
            {
                if (extension == ".sii" || extension == ".sui" || extension == ".mat")
                {
                    (wasModified, buffer) = SubstitutePathsInTextFormats(buffer, substitutions, extension);
                }
                else if (extension == ".tobj")
                {
                    (wasModified, buffer) = SubstitutePathsInTobj(buffer, substitutions);
                }
            }
            else
            {
                if (extension == ".sii" || extension == ".sui")
                {
                    buffer = SiiFile.Decode(buffer);
                }
            }

            File.WriteAllBytes(outputPath, buffer);
            return wasModified;
        }

        protected void WriteRenamedSummary(string outputRoot)
        {
            if (DryRun)
                return;
            if (renamedFiles.Count == 0)
                return;

            var path = Path.Combine(outputRoot, "_renamed.txt");
            path = PathUtils.IncrementPathIfExists(path);

            using var sw = new StreamWriter(path);
            foreach (var (before, after) in renamedFiles)
            {
                sw.WriteLine($"{before}\t{after}");
            }         
        }

        protected void WriteModifiedSummary(string outputRoot)
        {
            if (DryRun)
                return;
            if (modifiedFiles.Count == 0)
                return;

            var path = Path.Combine(outputRoot, "_modified.txt");
            path = PathUtils.IncrementPathIfExists(path);

            using var sw = new StreamWriter(path);
            foreach (var file in modifiedFiles)
            {
                sw.WriteLine(file);
            }
        }
    }
}
