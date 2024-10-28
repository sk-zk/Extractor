using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Extractor
{
    internal static class Util
    {
        public static readonly char[] InvalidPathChars =
            Path.GetInvalidFileNameChars().Except(['/', '\\']).ToArray();
        private static readonly char[] problematicControlChars =
            [(char)0x07, (char)0x08, (char)0x09, (char)0x0a, (char)0x0d, (char)0x1b];

        public static string SanitizePath(string input)
        {
            input = RemoveInitialSlash(input);
            return ReplaceCharsUnambiguously(input, InvalidPathChars);
        }

        public static string ReplaceControlChars(string input)
            => ReplaceChars(input, problematicControlChars, '�');

        public static string ReplaceChars(string input, char[] toReplace, char replacement)
        {
            var output = new StringBuilder();
            foreach (char c in input)
            {
                if (Array.IndexOf(toReplace, c) > -1)
                    output.Append(replacement);
                else
                    output.Append(c);
            }
            return output.ToString();
        }

        public static string ReplaceCharsUnambiguously(string input, char[] toReplace)
        {
            var output = new StringBuilder();
            foreach (char c in input)
            {
                if (Array.IndexOf(toReplace, c) > -1)
                    output.Append($"x{(int)c:X2}");
                else
                    output.Append(c);
            }
            return output.ToString();
        }

        public static string RemoveInitialSlash(string path)
        {
            if (path.StartsWith('/') && path != "/")
            {
                path = path[1..];
            }
            return path;
        }

        public static string RemoveTrailingSlash(string path)
        {
            if (path.EndsWith('/') && path != "/")
            {
                path = path[..^1];
            }
            return path;
        }

        public static void PrintRenameWarning(string original, string renamed)
        {
            Console.Out.WriteLine($"WARN: {ReplaceControlChars(original)} renamed to " +
                $"{ReplaceControlChars(renamed)}");
        }

        public static IEnumerable<string> GetAllScsFiles(string directory) 
            => Directory.EnumerateFiles(directory, "*.scs");
    }
}
