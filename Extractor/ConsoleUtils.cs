using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Extractor.PathUtils;

namespace Extractor
{
    internal class ConsoleUtils
    {
        public static void PrintRenameSummary(int renamed, int modified)
        {
            if (renamed > 0)
            {
                Console.WriteLine($"WARN: {renamed} {(renamed == 1 ? "file" : "files")} had to be renamed and " +
                    $"{modified} file{(renamed == 1 ? " was" : "s were")} modified accordingly.");
            }
        }

        public static void PrintExtractionMessage(string path, string scsName)
        {
            Console.WriteLine($"Extracting {ReplaceControlChars(Combine(scsName, path))} ...");
        }

        public static void PrintNoTopLevelError()
        {
            Console.Error.WriteLine("Top level directory is missing; " +
                "use --deep to scan contents for paths before extraction " +
                "or --partial to extract known paths");
        }
    }
}
