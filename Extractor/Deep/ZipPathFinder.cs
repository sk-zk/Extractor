using Extractor.Zip;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Extractor.Deep
{
    internal class ZipPathFinder
    {
        /// <summary>
        /// File paths that are referenced by files in this archive.
        /// </summary>
        public HashSet<string> ReferencedFiles { get; private set; }

        /// <summary>
        /// The ZIP archive.
        /// </summary>
        private readonly ZipReader reader;

        /// <summary>
        /// The <see cref="FilePathFinder"/> instance.
        /// </summary>
        private readonly FilePathFinder fpf;

        public ZipPathFinder(ZipReader reader) 
        {
            this.reader = reader;
            ReferencedFiles = [];
            fpf = new FilePathFinder([], ReferencedFiles, [], reader);
        }

        /// <summary>
        /// Scans the archive for path references and writes the results to <see cref="ReferencedFiles"/>.
        /// </summary>
        public void Find()
        {
            foreach (var (_, entry) in reader.Entries)
            {
                // Directory metadata; ignore
                if (entry.FileName.EndsWith('/'))
                {
                    continue;
                }

                var fileType = FileTypeHelper.PathToFileType(entry.FileName);
                if (FilePathFinder.IgnorableFileTypes.Contains(fileType))
                {
                    continue;
                }

                try
                {
                    using var ms = new MemoryStream();
                    reader.GetEntry(entry, ms);
                    var buffer = ms.ToArray();

                    var paths = fpf.FindPathsInFile(buffer, entry.FileName, fileType);
                    ReferencedFiles.UnionWith(paths);
                }
                catch (Exception)
                {
                    Debugger.Break();
                }
            }
        }
    }
}
