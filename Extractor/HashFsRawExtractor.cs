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
        public HashFsRawExtractor(string scsPath, bool overwrite) : base(scsPath, overwrite)
        {
        }

        public override void Extract(string[] startPaths, string destination)
        {
            DeleteJunkEntries();

            var scsName = Path.GetFileName(scsPath);
            Console.Out.WriteLine($"Extracting {scsName} ...");
            var outputDir = Path.Combine(destination, scsName);
            Directory.CreateDirectory(outputDir);

            if (Salt is not null)
            {
                Reader.Salt = Salt.Value;
            }

            foreach (var (key, entry) in Reader.Entries)
            {
                // subdirectory listings are useless because the file names are relative
                if (entry.IsDirectory) continue;

                var outputPath = Path.Combine(outputDir, key.ToString("x"));
                ExtractToFile(key.ToString("x"), outputPath, () => Reader.ExtractToFile(entry, "", outputPath));
            }
        }

        public override void PrintExtractionResult()
        {
            Console.WriteLine($"{extracted} extracted, {skipped} skipped, {duplicate} junk, {failed} failed");
        }

        public override void PrintPaths(string[] startPaths)
        {
            Console.WriteLine("--list and --raw cannot be combined.");
        }
    }
}
