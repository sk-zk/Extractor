﻿using System;
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
        static string startPath = "/";
        static bool printHelp = false;
        static bool rawMode = false;

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
                    $"Partial extraction, e.g. \"-p=/map\".",
                    x => { startPath = x; } },
                { "r|raw",
                    "Directly dumps the contained files with their hashed filenames rather than " +
                    "traversing the archive's directory tree." + 
                    "This allows for the extraction of base_cfg.scs, core.scs " +
                    "and\nlocale.scs, which do not include a top level directory listing.",
                    x => { rawMode = true; } },
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
                ExtractAllInDirectory(inputPath, startPath);
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
                    ExtractScs(inputPath, startPath);
                }
            }
        }

        private static void ExtractAllInDirectory(string directory, string start)
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
                    ExtractScs(scsPath, start);
                }
            }
        }

        private static void ExtractScs(string scsPath, string start)
        {
            var reader = HashFsReader.Open(scsPath, forceEntryHeadersAtEnd);

            switch (reader.EntryExists(start))
            {
                case EntryType.Directory:
                    ExtractDirectory(reader, start, Path.GetFileName(scsPath));
                    break;
                case EntryType.File:
                    ExtractSingleFile(reader, start);
                    break;
                case EntryType.NotFound:
                    break;
            }
        }

        private static void ExtractScsWithoutTopLevel(string scsPath)
        {
            var scsName = Path.GetFileName(scsPath);
            Console.Out.WriteLine($"Extracting {scsName} ...");
            var outputDir = Path.Combine(destination, scsName);
            Directory.CreateDirectory(outputDir);

            var reader = HashFsReader.Open(scsPath, forceEntryHeadersAtEnd);

            foreach (var (key, entry) in reader.GetEntries())
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

            foreach (var subdir in subdirs)
            {
                ExtractDirectory(reader, subdir, scsName);
            }
        }

        private static void ExtractFiles(HashFsReader reader, List<string> files)
        {
            foreach (var file in files)
            {
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
            var outputPath = Path.Combine(destination, path[1..]);
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
