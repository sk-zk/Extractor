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
        public static void PrintRenameWarning(string original, string renamed)
        {
            Console.Out.WriteLine($"WARN: {ReplaceControlChars(original)} renamed to " +
                $"{ReplaceControlChars(renamed)}");
        }

        public static void PrintRenameSummary(int renamed)
        {
            if (renamed > 0)
            {
                Console.WriteLine($"WARN: {renamed} file{(renamed == 1 ? " was" : "s were")} renamed. " +
                    $"Make sure to update references to affected paths accordingly.");
            }
        }

        public static void PrintExtractionMessage(string path, string scsName)
        {
            if (!path.StartsWith('/'))
                path = $"/{path}";

            Console.WriteLine($"Extracting {ReplaceControlChars($"{scsName}{path}")} ...");
        }
    }
}
