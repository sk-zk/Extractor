using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Extractor
{
    public class HashFsRawExtractor : HashFsExtractor
    {
        public HashFsRawExtractor(string scsPath, bool overwrite, ushort? salt) 
            : base(scsPath, overwrite, salt)
        {
        }

        public override void Extract(IList<string> pathFilter, string destination)
        {
            DeleteJunkEntries();

            var scsName = Path.GetFileName(scsPath);
            Console.Out.WriteLine($"Extracting {scsName} ...");
            var outputDir = Path.Combine(destination, scsName);
            Directory.CreateDirectory(outputDir);

            foreach (var (key, entry) in Reader.Entries)
            {
                // subdirectory listings are useless because the file names are relative
                if (entry.IsDirectory) continue;

                var outputPath = Path.Combine(outputDir, key.ToString("x"));
                ExtractToDisk(entry, key.ToString("x"), outputPath);
            }
        }

        public override void PrintExtractionResult()
        {
            Console.WriteLine($"{extracted} extracted, {skipped} skipped, {duplicate} junk, {failed} failed");
        }

        public override void PrintPaths(IList<string> pathFilter, bool includeAll)
        {
            Console.WriteLine("--list and --raw cannot be combined.");
        }
    }
}
