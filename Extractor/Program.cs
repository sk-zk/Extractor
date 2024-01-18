using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TruckLib.HashFs;
using Mono.Options;
using Ionic.Zlib;

namespace Extractor
{
    class Program
    {
        static string destination = "./extracted/";
        static bool skipIfExists = false;
        static bool forceEntryHeadersAtEnd = false;
        static bool listEntryHeaders = false;
        static string inputPath = ".";
        static bool extractAll = false;
        static string[] startPaths = new[] { "/" };
        static bool printHelp = false;
        static bool rawMode = false;
        static bool tree = false;
        static ushort? salt = null;

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            var p = new OptionSet()
            {
                { "<>",
                    "Path",
                    x => { inputPath = x; } },
                { "a|all",
                    "Extracts every .scs archive in the directory.",
                    x => { extractAll = true; } },
                { "d=|dest=",
                    $"The output directory.\nDefault: {destination}",
                    x => { destination = x; } },
                { "headers-at-end",
                    "Ignores what the archive header says and reads entry headers " +
                    "from the end of the file.",
                    x => { forceEntryHeadersAtEnd = true; } },
                { "list",
                    "Lists entry headers and exits.",
                    x => { listEntryHeaders = true; } },
                { "p=|partial=",
                    "Partial extraction, e.g.:\n" +
                    "-p=/map\n" +
                    "-p=/def,/map\n" +
                    "-p=/def/world/road.sii",
                    x => { startPaths = x.Split(","); } },
                 { "paths=",
                    "Same as --partial, but expects a text file containing paths to extract, " +
                    "separated by newlines.",
                    x => { startPaths = LoadStartPathsFromFile(x); } },
                { "r|raw",
                    "Directly dumps the contained files with their hashed filenames rather than " +
                    "traversing the archive's directory tree. " + 
                    "This allows for the extraction of base_cfg.scs, core.scs " +
                    "and\nlocale.scs, which do not include a top level directory listing.",
                    x => { rawMode = true; } },
                { "salt=",
                    "Ignores the salt in the archive header and uses this one instead.",
                    x => { salt = ushort.Parse(x); } },
                { "s|skip-existing",
                    "Don't overwrite existing files.",
                    x => { skipIfExists = true; } },
                { "tree",
                    "Prins the directory tree and exits. Can be combined with --partial, " +
                    "--paths, and --all.",
                    x => { tree = true; } },
                { "?|h|help",
                    $"Prints this message and exits.",
                    x => { printHelp = true; } },
            };
            p.Parse(args);

            if (printHelp || args.Length == 0)
            {
                Console.WriteLine("extractor path [options]\n");
                Console.WriteLine("Options:");
                p.WriteOptionDescriptions(Console.Out);
                return;
            }

            if (!extractAll && !File.Exists(inputPath))
            {
                Console.Error.WriteLine($"{inputPath} is not a file or does not exist.");
                Environment.Exit(-1);
            }
            else if (extractAll & !Directory.Exists(inputPath))
            {
                Console.Error.WriteLine($"{inputPath} is not a directory or does not exist.");
                Environment.Exit(-1);
            }

            if (listEntryHeaders)
            {
                ListEntryHeaders(inputPath);
                return;
            }

            if (tree)
            {
                if (extractAll)
                {
                    foreach (var scsPath in GetAllScsFiles(inputPath))
                    {
                        Tree.PrintTree(scsPath, startPaths, forceEntryHeadersAtEnd);
                    }
                }
                else
                {
                    Tree.PrintTree(inputPath, startPaths, forceEntryHeadersAtEnd);
                }
                return;
            }

            if (extractAll)
            {
                ExtractAllInDirectory(inputPath, startPaths);
            }
            else
            {
                if (rawMode)
                {
                    ExtractRaw(inputPath);
                }
                else
                {
                    ExtractScs(inputPath, startPaths);
                }
            }
        }

        private static void ListEntryHeaders(string scsPath)
        {
            var reader = HashFsReader.Open(scsPath, forceEntryHeadersAtEnd);
            Console.WriteLine($"  {"Offset",-10}  {"Hash",-16}  {"Cmp. Size",-10}  {"Uncmp.Size",-10}  {"CRC",-8}");
            foreach (var (_, entry) in reader.Entries)
            {
                Console.WriteLine($"{(entry.IsDirectory ? "*" : " ")} " +
                    $"{entry.Offset,10}  {entry.Hash,16:x}  {entry.CompressedSize,10}  {entry.Size,10}  {entry.Crc,8:X}");
            }
        }

        private static string[] LoadStartPathsFromFile(string file)
        {
            if (!File.Exists(file))
            {
                Console.WriteLine($"File {file} does not exist");
            }
            return File.ReadAllLines(file).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        }

        private static void ExtractAllInDirectory(string directory, string[] startPaths)
        {
            foreach (var scsPath in GetAllScsFiles(directory))
            {
                if (rawMode)
                {
                    ExtractRaw(directory);
                }
                else
                {
                    ExtractScs(scsPath, startPaths, false);
                }
            }
        }

        private static IEnumerable<string> GetAllScsFiles(string directory) => 
            Directory.EnumerateFiles(directory, "*.scs");

        private static void ExtractScs(string scsPath, string[] startPaths, 
            bool printNotFoundMessage = true)
        {
            var reader = HashFsReader.Open(scsPath, forceEntryHeadersAtEnd);
            if (salt is not null)
            {
                reader.Salt = salt.Value;
            }

            foreach (var startPath in startPaths)
            {
                switch (reader.EntryExists(startPath))
                {
                    case EntryType.Directory:
                        ExtractDirectory(reader, startPath, Path.GetFileName(scsPath));
                        break;
                    case EntryType.File:
                        ExtractSingleFile(reader, startPath);
                        break;
                    case EntryType.NotFound:
                        if (startPath == "/")
                        {
                            Console.WriteLine("Top level directory is missing; " +
                                "try a partial extraction or use --raw to dump entries");
                        }
                        else if (printNotFoundMessage)
                        {
                            Console.WriteLine($"File or directory listing {startPath} does not exist");
                        }
                        break;
                }
            }
        }

        private static void ExtractRaw(string scsPath)
        {
            var scsName = Path.GetFileName(scsPath);
            Console.Out.WriteLine($"Extracting {scsName} ...");
            var outputDir = Path.Combine(destination, scsName);
            Directory.CreateDirectory(outputDir);

            var reader = HashFsReader.Open(scsPath, forceEntryHeadersAtEnd);
            if (salt is not null)
            {
                reader.Salt = salt.Value;
            }

            var seenOffsets = new HashSet<ulong>();

            foreach (var (key, entry) in reader.Entries)
            {
                if (seenOffsets.Contains(entry.Offset)) continue;
                seenOffsets.Add(entry.Offset);

                // subdirectory listings are useless because the file names are relative
                if (entry.IsDirectory) continue;

                var outputPath = Path.Combine(outputDir, key.ToString("x"));
                if (skipIfExists && File.Exists(outputPath)) return;

                try
                {
                    reader.ExtractToFile(entry, outputPath);
                } 
                catch (ZlibException zlex)
                {
                    Console.WriteLine($"Unable to extract entry at offset {entry.Offset}:");
                    Console.WriteLine(zlex.Message);
                }
            }
        }

        private static void ExtractDirectory(HashFsReader reader, string directory, string scsName)
        {
            Console.Out.WriteLine($"Extracting {scsName}{directory} ...");

            var (subdirs, files) = reader.GetDirectoryListing(directory);
            Directory.CreateDirectory(Path.Combine(destination, directory[1..]));
            ExtractFiles(reader, files);
            ExtractSubdirectories(reader, subdirs, scsName);
        }

        private static void ExtractSubdirectories(HashFsReader reader, List<string> subdirs, string scsName)
        {
            foreach (var subdir in subdirs)
            {
                var type = reader.EntryExists(subdir);
                switch (type)
                {
                    case EntryType.File:
                        // TODO
                        throw new NotImplementedException();
                    case EntryType.NotFound:
                        Console.WriteLine($"Directory {subdir} is referenced in a directory listing " +
                            $"but could not be found in the archive");
                        continue;
                }
                ExtractDirectory(reader, subdir, scsName);
            }
        }

        private static void ExtractFiles(HashFsReader reader, List<string> files)
        {
            foreach (var file in files)
            {
                var type = reader.EntryExists(file);
                switch (type)
                {
                    case EntryType.NotFound:
                        Console.WriteLine($"File {file} is referenced in a directory listing" +
                            $" but could not be found in the archive");
                        continue;
                    case EntryType.Directory:
                        // usually safe to ignore because it just points to the directory itself again
                        continue;
                }

                // The directory listing of core.scs only lists itself, but as a file, breaking everything
                if (file == "/") continue;

                var outputPath = Path.Combine(destination, file[1..]);
                if (skipIfExists && File.Exists(outputPath)) return;

                try
                {
                    reader.ExtractToFile(file, outputPath);
                }
                catch (ZlibException zlex)
                {
                    Console.WriteLine($"Unable to extract entry {file}:");
                    Console.WriteLine(zlex.Message);
                }
            }
        }

        private static void ExtractSingleFile(HashFsReader reader, string path)
        {
            if (path.StartsWith("/"))
            {
                path = path[1..];
            }

            var outputPath = Path.Combine(destination, path);
            if (skipIfExists && File.Exists(outputPath)) return;

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            Console.WriteLine($"Extracting {path} ...");
            try
            {
                reader.ExtractToFile(path, outputPath);
            }
            catch (ZlibException zlex)
            {
                Console.WriteLine($"Unable to extract {path}:");
                Console.WriteLine(zlex.Message);
            }
        }

    }
}
