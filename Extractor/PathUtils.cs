using Extractor.Deep;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Extractor
{
    public static class PathUtils
    {
        // Sanitization configuration
        public static bool LegacyReplaceMode { get; set; } = false; // true = legacy hex (xNN) replacements
        public static double NameCounterThreshold { get; set; } = 0.10; // 10% invalid chars
        public static string CounterPrefix { get; set; } = "F";
        public static int CounterWidth { get; set; } = 8;
        private static int GlobalNameCounter = 0;
        private static readonly ConcurrentDictionary<string, int> NameIdMap = new();

        private static readonly char[] ProblematicControlChars =
            [(char)0x07, (char)0x08, (char)0x09, (char)0x0a, (char)0x0d, (char)0x1b, '\u200b'];

        public static readonly char[] ProblematicUnicodeChars = [
            // whitespace / invisible characters
            '\u00a0', '\u00ad', '\u034f', '\u061c', '\u180e', '\u2000',
            '\u2001', '\u2002', '\u2003', '\u2004', '\u2005', '\u2006',
            '\u2007', '\u2008', '\u2009', '\u200A', '\u200B', '\u200C',
            '\u200D', '\u202F', '\u205F', '\u2060', '\u2061', '\u2062',
            '\u2063', '\u2064', '\u2065', '\u2800', '\u3000', '\ufeff',
        ];

        public static readonly char[] InvalidPathChars =
            Path.GetInvalidFileNameChars()
            .Except(['/', '\\'])
            .Concat(ProblematicUnicodeChars)
            .Order()
            .ToArray();

        /// <summary>
        /// Names which, in Windows, are reserved and cannot be used as the name of
        /// a file or directory
        /// </summary>
        private static readonly FrozenSet<string> ReservedNames = new HashSet<string> {
            "con", "prn", "aux", "nul",
            "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
            "com¹", "com²", "com³",
            "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
            "lpt¹", "lpt²", "lpt³"
        }.ToFrozenSet();

        private static readonly bool IsWindows = OperatingSystem.IsWindows();

        /// <summary>
        /// Replaces invalid or problematic portions of a HashFS or ZIP file path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="invalidPathChars">Characters which cannot be used in paths.
        /// Defaults to <see cref="InvalidPathChars"/>.</param>
        /// <param name="isWindows">Whether the path should be sanitized for Windows.</param>
        /// <returns>The sanitized path.</returns>
        public static string SanitizePath(string path, char[] invalidPathChars = null, bool? isWindows = null)
        {
            var invalid = invalidPathChars ?? InvalidPathChars;
            isWindows ??= IsWindows;

            var parts = path.Split('/');
            if (parts.Length == 0)
                return path;

            // Find last non-empty segment (file name); if none, treat as directory path
            int lastIdx = parts.Length - 1;
            while (lastIdx > 0 && parts[lastIdx].Length == 0)
                lastIdx--;

            bool hasFileName = parts[lastIdx].Length > 0;

            for (int i = 0; i < parts.Length; i++)
            {
                // prevent traversing upwards with ".."
                if (parts[i] == "..")
                {
                    parts[i] = "__";
                    continue;
                }

                bool replaceWholeSegment = false;
                if (!LegacyReplaceMode)
                {
                    var seg = parts[i];
                    int invalidCount = 0;
                    foreach (var ch in seg)
                    {
                        if (Array.BinarySearch(invalid, ch) > -1)
                            invalidCount++;
                    }
                    if (seg.Length > 0)
                    {
                        var ratio = (double)invalidCount / seg.Length;
                        replaceWholeSegment = ratio >= NameCounterThreshold;
                    }
                }

                if (replaceWholeSegment)
                {
                    if (i == lastIdx && hasFileName)
                    {
                        // Replace the entire file name with a counter, but preserve extension
                        var baseName = Path.GetFileNameWithoutExtension(parts[i]);
                        var ext = Path.GetExtension(parts[i]);
                        parts[i] = GetTokenForKey(baseName, "F") + ext;
                    }
                    else
                    {
                        // Replace directory segment entirely with a D-prefixed counter
                        parts[i] = GetTokenForKey(parts[i], "D");
                    }
                }
                else
                {
                    parts[i] = ReplaceCharsUnambiguously(parts[i], invalid);
                }

                // append underscore to reserved names in Windows (after replacement)
                if (isWindows.Value && ReservedNames.Contains(
                    Path.GetFileNameWithoutExtension(parts[i]).ToLowerInvariant()))
                {
                    parts[i] = AppendBeforeExtension(parts[i], "_");
                }
            }

            return string.Join('/', parts);
        }

        public static string ReplaceControlChars(string input)
            => ReplaceChars(input, ProblematicControlChars, '�');

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
            // Below 10% threshold (or legacy mode), we always want the legacy per-char hex replacement.
            return ReplaceCharsHex(input, toReplace);
        }

        private static string ReplaceCharsHex(string input, char[] toReplace)
        {
            var output = new StringBuilder();
            foreach (char c in input)
            {
                if (Array.BinarySearch(toReplace, c) > -1)
                    output.Append($"x{(int)c:X2}");
                else
                    output.Append(c);
            }
            return output.ToString();
        }


        private static string GetTokenForKey(string key, string prefix)
        {
            // Thread-safe global counter shared across the whole process
            var current = NameIdMap.GetOrAdd(key, _ => Interlocked.Increment(ref GlobalNameCounter) - 1);
            return $"{prefix}{current.ToString($"D{CounterWidth}")}";
        }

        // For tests and controlled runs
        public static void ResetNameCounter()
        {
            GlobalNameCounter = 0;
            NameIdMap.Clear();
        }

        public static string RemoveInitialSlash(string path)
        {
            if (path.StartsWith('/') && path != "/")
            {
                path = path[1..];
            }
            return path;
        }

        public static void RemoveInitialSlash(ref string path)
        {
            if (path.StartsWith('/') && path != "/")
            {
                path = path[1..];
            }
        }

        public static string RemoveTrailingSlash(string path)
        {
            if (path.EndsWith('/') && path != "/")
            {
                path = path[..^1];
            }
            return path;
        }

        public static void RemoveTrailingSlash(ref string path)
        {
            if (path.EndsWith('/') && path != "/")
            {
                path = path[..^1];
            }
        }

        public static void EnsureHasInitialSlash(ref string path)
        {
            if (!path.StartsWith('/'))
            {
                path = '/' + path;
            }
        }

        public static string RemoveNonAsciiOrInvalidChars(string input)
        {
            // Preserve normal Unicode letters/digits and common punctuation.
            // Remove only control characters and characters we explicitly mark as invalid
            // (includes invisible/whitespace trickery and OS-invalid filename chars).
            var output = new StringBuilder();
            foreach (char c in input)
            {
                if (char.IsControl(c))
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

            RemoveTrailingSlash(ref filePath);

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

        public static bool ResemblesPath(string str)
        {
            if (string.IsNullOrEmpty(str))
                return false;

            var hasSlashOrDot = false;
            for (int i = 0; i < str.Length; i++)
            {
                var c = str[i];

                if (c == '/')
                {
                    if (i > 0 && str[i - 1] == '/')
                    {
                        // don't allow "//"
                        return false;
                    }
                    hasSlashOrDot = true;
                }
                else if (c == '.')
                {
                    hasSlashOrDot = true;
                }
            }
            return hasSlashOrDot;
        }           

        /// <summary>
        /// Converts a string containing lines separated by <c>\n</c> or <c>\r\n</c> to a HashSet.
        /// </summary>
        /// <param name="input">The input string.</param>
        /// <returns>A HashSet populated with the lines contained in the input string.</returns>
        public static PotentialPaths LinesToHashSet(string input)
        {
            PotentialPaths output = [];

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
            var rootDir = new Tree.Directory { Path = "/" };

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
                if (!path.EndsWith('/'))
                {
                    subdir.Files.Add(path);
                }
            }

            return rootDir;
        }

        public static IEnumerable<string> GetAllScsFiles(string directory) 
            => Directory.EnumerateFiles(directory, "*.scs");

        /// <summary>
        /// If the given path exists, a number is added to its end which increases
        /// until a path is found which does not yet exist.
        /// </summary>
        /// <param name="path">The path to increment.</param>
        /// <returns>The original path if it did not exist, or an incremented path if it did.</returns>
        public static string IncrementPathIfExists(string path)
        {
            if (!File.Exists(path))
                return path;

            for (int i = 2; i < 9999; i++)
            {
                var newPath = AppendBeforeExtension(path, i.ToString());
                if (!File.Exists(newPath))
                    return newPath;
            }
            throw new IOException("bruh.");
        }

        /// <summary>
        /// Appends a string to a path before the extension.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="addition">The string to add before the extension.</param>
        /// <returns>The modified path.</returns>
        public static string AppendBeforeExtension(string path, string addition)
        {
            var dot = path.LastIndexOf('.');
            if (dot == -1)
                return $"{path}{addition}";
            return $"{path[..dot]}{addition}{path[dot..]}";
        }
    }
}

