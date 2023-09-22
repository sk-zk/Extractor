using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TruckLib.HashFs;
using Mono.Options;

namespace Extractor
{
    class Program
    {
        static string destination = "./extracted/";
        static bool skipIfExists = false;
        static bool forceEntryHeadersAtEnd = false;
        static string inputPath = ".";
        static bool extractAll = false;
        static string[] startPaths = new[] { "/" };
        static bool printHelp = false;
        static bool rawMode = false;
        static ushort? salt = null;

        static void Main(string[] args)
        {

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
                    "Ignores what the archive header says and reads entry headers from the end of the file.",
                    x => { forceEntryHeadersAtEnd = true; } },
                { "p=|partial=",
                    "Partial extraction, e.g.:\n" +
                    "-p=/map\n" +
                    "-p=/def,/map\n" +
                    "-p=/def/world/road.sii",
                    x => { startPaths = x.Split(","); } },
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

            if (extractAll)
            {
                ExtractAllInDirectory(inputPath, startPaths);
            }
            else
            {
                if (!File.Exists(inputPath))
                {
                    Console.Error.WriteLine($"\"{inputPath}\" is not a file or does not exist.");
                    Environment.Exit(-1);
                }
                if (rawMode)
                {
                    ExtractScsWithoutTopLevel(inputPath);
                }
                else
                {
                    ExtractScs(inputPath, startPaths);
                }
            }
        }

        private static void ExtractAllInDirectory(string directory, string[] startPaths)
        {
            if (!Directory.Exists(directory))
            {
                Console.Error.WriteLine($"\"{directory}\" is not a directory or does not exist.");
                Environment.Exit(-1);
            }

            foreach (var scsPath in Directory.EnumerateFiles(directory, "*.scs"))
            {
                if (rawMode)
                {
                    ExtractScsWithoutTopLevel(directory);
                }
                else
                {
                    ExtractScs(scsPath, startPaths);
                }
            }
        }

        private static void ExtractScs(string scsPath, string[] startPaths)
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
                        else
                        {
                            Console.WriteLine($"File or directory listing {startPath} does not exist");
                        }
                        break;
                }
            }
        }

        private static void ExtractScsWithoutTopLevel(string scsPath)
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

            foreach (var (key, entry) in reader.Entries)
            {
                if (entry.IsDirectory)
                {
                    // locale.scs contains subdirectory listings,
                    // but they're useless because the file names are relative
                    continue;
                }
                var outputPath = Path.Combine(outputDir, key.ToString("x"));
                if (skipIfExists && File.Exists(outputPath))
                {
                    return;
                }
                reader.ExtractToFile(entry, outputPath);
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
                if (file == "/")
                {
                    continue;
                }

                var outputPath = Path.Combine(destination, file[1..]);
                if (skipIfExists && File.Exists(outputPath))
                {
                    return;
                }

                reader.ExtractToFile(file, outputPath);
            }
        }

        private static void ExtractSingleFile(HashFsReader reader, string path)
        {
            if (path.StartsWith("/"))
            {
                path = path[1..];
            }
            var outputPath = Path.Combine(destination, path);
            if (skipIfExists && File.Exists(outputPath))
            {
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            Console.WriteLine($"Extracting {path} ...");
            reader.ExtractToFile(path, outputPath);
        }

    }
}
