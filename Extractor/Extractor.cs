using Extractor.Tree;
using System;
using System.Collections.Generic;

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
        /// <param name="startPaths">A list of paths to extract. Pass ["/"] to extract the entire archive.</param>
        /// <param name="destination">The directory to which the extracted files will be written.</param>
        public abstract void Extract(string[] startPaths, string destination);

        public abstract Directory GetDirectoryTree(string root);

        public abstract void PrintPaths(string[] startPaths);

        public abstract void PrintContentSummary();

        public abstract void PrintExtractionResult();

        public abstract void Dispose();
    }
}