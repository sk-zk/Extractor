using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TruckLib.HashFs;

namespace Extractor
{
    internal static class Tree
    {
        public static void PrintTree(IHashFsReader reader, string[] startPaths)
        {
            var scsName = Path.GetFileName(reader.Path);
            foreach (var startPath in startPaths)
            {
                var entryType = reader.EntryExists(startPath);
                var parts = startPath.Split("/", StringSplitOptions.RemoveEmptyEntries);
                switch (entryType)
                {
                    case EntryType.Directory:
                        PrintWithColor(scsName, ConsoleColor.White);
                        if (startPath == "/")
                        {
                            PrintDirectory(reader, startPath, 1);
                        }
                        else
                        {
                            PrintPathParts(parts);
                            PrintDirectory(reader, startPath, parts.Length + 1);
                        }
                        break;
                    case EntryType.File:
                        PrintWithColor(scsName, ConsoleColor.White);
                        PrintPathParts(parts[..^1]);
                        WriteIndent(parts.Length + 1, true);
                        Console.WriteLine(parts[^1]);
                        break;
                    case EntryType.NotFound:
                        if (startPath == "/")
                        {
                            Console.WriteLine("Top level directory listing is missing.");
                        }
                        break;
                }
            }
        }

        private static void PrintPathParts(string[] parts)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                WriteIndent(i + 1);
                PrintDirectoryName(parts[i]);
            }
        }

        private static void PrintDirectoryName(string name)
        {
            PrintWithColor($"[{name}]", ConsoleColor.Yellow);
        }

        private static void PrintDirectory(IHashFsReader reader, string path, int indent)
        {
            if (path.EndsWith('/'))
                path = path[..^1];
            IEntry entry;
            try
            {
                entry = reader.GetEntry(path);
            }
            catch (KeyNotFoundException)
            {
                return;
            }
            // The subdir list may contain patsh which have not
            // been marked as a directory.
            entry.IsDirectory = true;

            var (subdirs, files) = reader.GetDirectoryListing(path);
            foreach (var subdir in subdirs)
            {
                WriteIndent(indent);
                var withoutLastSlash = subdir[..^1];
                PrintDirectoryName(withoutLastSlash[(withoutLastSlash.LastIndexOf('/') + 1)..]);
                PrintDirectory(reader, subdir, indent + 1);
            }
            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                WriteIndent(indent, i == files.Count - 1);
                Console.WriteLine(file[(file.LastIndexOf('/') + 1)..]);
            }
        }

        private static void PrintWithColor(string str, ConsoleColor color)
        {
            var prevConsoleColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(str);
            Console.ForegroundColor = prevConsoleColor;
        }

        private static void WriteIndent(int indent, bool isLast = false)
        {
            for (int i = 0; i < indent; i++)
            {
                if (i == indent - 1)
                {
                    if (isLast)
                    {
                        Console.Write(" └╴");
                    }
                    else
                    {
                        Console.Write(" ├╴");
                    }
                }
                else
                {
                    Console.Write(" │ ");
                }
            }
        }
    }
}
