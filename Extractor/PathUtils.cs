using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Extractor
{
    public static class PathUtils
    {
        public static readonly char[] InvalidPathChars =
            Path.GetInvalidFileNameChars().Except(['/', '\\']).ToArray();
        private static readonly char[] problematicControlChars =
            [(char)0x07, (char)0x08, (char)0x09, (char)0x0a, (char)0x0d, (char)0x1b, '\u200b'];

        /// <summary>
        /// Replaces invalid or problematic portions of a HashFS or ZIP file path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="invalidPathChars">Characters which cannot be used in paths.
        /// Defaults to <see cref="InvalidPathChars"/>.</param>
        /// <returns>The sanitized path.</returns>
        public static string SanitizePath(string path, char[] invalidPathChars = null)
        {
            path = ReplaceCharsUnambiguously(path, invalidPathChars ?? InvalidPathChars);
            // prevent traversing upwards with ".."
            path = string.Join('/', path.Split(['/', '\\']).Select(x => x == ".." ? "__" : x));
            return path;
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

        public static string RemoveNonAsciiOrInvalidChars(string input)
        {
            // TODO don't remove non-chicanery unicode chars like 'ß' etc.
            var output = new StringBuilder();
            foreach (char c in input)
            {
                if (char.IsControl(c))
                    continue;
                if (!char.IsAscii(c))
                    continue;
                if (Array.IndexOf(InvalidPathChars, c) > -1)
                    continue;

                output.Append(c);
            }
            return output.ToString();
        }

        public static string GetParent(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) 
               return "";

            filePath = RemoveTrailingSlash(filePath);

            var lastSlash = filePath.LastIndexOf('/');

            string parent;
            if (lastSlash == -1)
                parent = "";
            else if (lastSlash == 0)
                parent = "/";
            else
                parent = filePath[..filePath.LastIndexOf('/')];

            return parent;
        }

        public static string Combine(string scsName, string archivePath)
        {
            if (archivePath.StartsWith('/'))
            {
                return $"{scsName}{archivePath}";
            }
            return $"{scsName}/{archivePath}";
        }

        public static bool ResemblesPath(string str) =>
            !string.IsNullOrEmpty(str)
            && !str.Contains("//")
            && (str.Contains('/')
            || str.Contains('.'));

        /// <summary>
        /// Converts a string containing lines separated by <c>\n</c> or <c>\r\n</c> to a HashSet.
        /// </summary>
        /// <param name="input">The input string.</param>
        /// <returns>A HashSet populated with the lines contained in the input string.</returns>
        public static HashSet<string> LinesToHashSet(string input)
        {
            HashSet<string> output = [];

            int start = 0;
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c == '\n' || (c == '\r' && i + 1 < input.Length && input[i + 1] == '\n'))
                {
                    var line = input.AsSpan(start, i - start);
                    output.Add(line.ToString());

                    if (c == '\r')
                        i++;
                    start = i + 1;
                }
            }

            if (start < input.Length)
                output.Add(input.AsSpan(start).ToString());

            return output;
        }

        public static Tree.Directory PathListToTree(string root, IEnumerable<string> paths)
        {
            var rootDir = new Tree.Directory();
            rootDir.Path = "/";

            foreach (var path in paths)
            {
                if (root != "/" && !path.StartsWith(root))
                    continue;

                var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                Tree.Directory subdir = rootDir;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (subdir.Subdirectories.TryGetValue(parts[i], out var dir))
                    {
                        subdir = dir;
                    }
                    else
                    {
                        var newDir = new Tree.Directory();
                        newDir.Path = "/" + string.Join('/', parts[0..(i + 1)]);
                        subdir.Subdirectories.Add(parts[i], newDir);
                        subdir = newDir;
                    }
                }
                subdir.Files.Add(path);
            }

            return rootDir;
        }

        public static IEnumerable<string> GetAllScsFiles(string directory) 
            => Directory.EnumerateFiles(directory, "*.scs");
    }
}
