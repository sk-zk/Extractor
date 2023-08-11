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

        static void Main(string[] args)
        {
            string path = ".";
            bool all = false;
            string start = "/";
            bool help = false;
            bool raw = false;
            var p = new OptionSet()
            {
                { "<>",
                    "Path",
                    x => { path = x; } },
                { "a|all",
                    "Extracts every .scs archive in the directory.",
                    x => { all = true; } },
                { "d=",
                    $"The output directory.\nDefault: {destination}",
                    x => { destination = x; } },
                { "p=",
                    $"Partial extraction, e.g. \"-p=/map\".",
                    x => { start = x; } },
                { "r|raw",
                    "Directly dumps the contained files with their hashed filenames rather than " +
                    "traversing the archive's directory tree." + 
                    "This allows for the extraction of base_cfg.scs, core.scs " +
                    "and\nlocale.scs, which do not include a top level directory listing.",
                    x => { raw = true; } },
                { "?|h|help",
                    $"Prints this message.",
                    x => { help = true; } },
            };
            p.Parse(args);
            if (help || args.Length == 0)
            {
                Console.WriteLine("extractor path [options]\n");
                Console.WriteLine("Options:");
                p.WriteOptionDescriptions(Console.Out);
                return;
            }

            if (all)
            {
                ExtractAllInDirectory(path, start, raw);
            }
            else
            {
                if (!File.Exists(path))
                {
                    Console.Error.WriteLine($"\"{path}\" is not a file or does not exist.");
                    Environment.Exit(-1);
                }
                if (raw)
                {
                    ExtractScsWithoutTopLevel(path);
                }
                else
                {
                    ExtractScs(path, start);
                }
            }
        }

        private static void ExtractAllInDirectory(string directory, string start, bool raw = false)
        {
            if (!Directory.Exists(directory))
            {
                Console.Error.WriteLine($"\"{directory}\" is not a directory or does not exist.");
                Environment.Exit(-1);
            }
            foreach (var scsPath in Directory.EnumerateFiles(directory, "*.scs"))
            {
                if (raw)
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
            var reader = HashFsReader.Open(scsPath);

            switch (reader.EntryExists(start))
            {
                case EntryType.Directory:
                    ExtractDirectory(reader, start, Path.GetFileName(scsPath));
                    break;
                case EntryType.File:
                    ExtractSingleFile(start, reader);
                    break;
                case EntryType.NotFound:
                    break;
            }
        }

        private static void ExtractScsWithoutTopLevel(string scsPath)
        {
            var scsName = Path.GetFileName(scsPath);
            var outputDir = Path.Combine(destination, scsName);
            Directory.CreateDirectory(outputDir);

            var reader = HashFsReader.Open(scsPath);

            var associatedToDirectories = new HashSet<string>();
            foreach (var (key, entry) in reader.GetEntries())
            {
                if (entry.IsDirectory)
                {
                    // locale.scs contains subdirectory listings,
                    // but they're useless because the file names are relative
                    continue;
                }
                reader.ExtractToFile(entry, Path.Combine(outputDir, key.ToString("x")));
            }
        }

        private static void ExtractDirectory(HashFsReader r, string directory, string scsName)
        {
            Console.Out.WriteLine($"Extracting {scsName}{directory} ...");

            var (subdirs, files) = r.GetDirectoryListing(directory);
            Directory.CreateDirectory(Path.Combine(destination, directory[1..]));
            ExtractFiles(r, files);

            foreach (var subdir in subdirs)
                ExtractDirectory(r, subdir, scsName);
        }

        private static void ExtractFiles(HashFsReader r, List<string> files)
        {
            foreach (var file in files)
            {
                // The directory listing of core.scs only lists itself, but as a file, breaking everything
                if (file == "/")
                {
                    continue;
                }
                var path = Path.Combine(destination, file[1..]);
                r.ExtractToFile(file, path);
            }
        }

        private static void ExtractSingleFile(string path, HashFsReader reader)
        {
            var outputPath = Path.Combine(destination, path[1..]);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            Console.WriteLine($"Extracting {path} ...");
            reader.ExtractToFile(path, outputPath);
        }

    }
}
