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
        private int duplicate = 0;

        public HashFsRawExtractor(string scsPath, bool overwrite) : base(scsPath, overwrite)
        {
        }

        public override void Extract(string[] startPaths, string destination)
        {
            var scsName = Path.GetFileName(scsPath);
            Console.Out.WriteLine($"Extracting {scsName} ...");
            var outputDir = Path.Combine(destination, scsName);
            Directory.CreateDirectory(outputDir);

            if (Salt is not null)
            {
                Reader.Salt = Salt.Value;
            }

            extracted = 0;
            skipped = 0;
            failed = 0;

            var seenOffsets = new HashSet<ulong>();

            foreach (var (key, entry) in Reader.Entries)
            {
                if (seenOffsets.Contains(entry.Offset))
                {
                    duplicate++;
                    continue;
                }
                seenOffsets.Add(entry.Offset);

                // subdirectory listings are useless because the file names are relative
                if (entry.IsDirectory) continue;

                var outputPath = Path.Combine(outputDir, key.ToString("x"));
                ExtractToFile(key.ToString("x"), outputPath, () => Reader.ExtractToFile(entry, "", outputPath));
            }
        }

        public override void PrintSummary()
        {
            Console.WriteLine($"{extracted} extracted, {skipped} skipped, {duplicate} duplicates, {failed} failed");
        }
    }
}
