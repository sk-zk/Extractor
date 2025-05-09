using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TruckLib.Sii;

namespace Extractor
{
    /// <summary>
    /// Base class for archive extractors.
    /// </summary>
    public abstract class Extractor : IDisposable
    {
        /// <summary>
        /// Gets or sets whether existing files should be overwritten.
        /// </summary>
        public bool Overwrite { get; set; } = true;

        /// <summary>
        /// The absolute path of the archive file.
        /// </summary>
        protected string scsPath;

        public Extractor(string scsPath, bool overwrite) 
        { 
            this.scsPath = scsPath;
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
            var isSii = extension == ".sii";
            var isOtherTextFormat = extension == ".sui" || extension == ".mat";

            if (isSii || isOtherTextFormat)
            {
                if (isSii)
                {
                    buffer = SiiFile.Decode(buffer);
                }

                var content = Encoding.UTF8.GetString(buffer);
                (content, wasModified) = TextUtils.ReplaceRenamedPaths(content, substitutions);

                buffer = Encoding.UTF8.GetBytes(content);
            }

            File.WriteAllBytes(outputPath, buffer);
            return wasModified;
        }
    }
}
