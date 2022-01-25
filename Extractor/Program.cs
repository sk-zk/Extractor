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
            var p = new OptionSet()
            {
                { "<>",
                    "Path",
                    x => { path = x; } },
                { "a|all",
                    "Extracts every .scs file in the directory.",
                    x => { all = true; } },
                { "d=",
                    $"The output directory.\nDefault: {destination}",
                    x => { destination = x; } },
                { "p=",
                    $"Partial extraction, e.g. \"-p=/map\".",
                    x => { start = x; } },
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
                ExtractAllInDirectory(path, start);
            }
            else
            {
                if (!File.Exists(path))
                {
                    Console.Error.WriteLine($"\"{path}\" is not a file or does not exist.");
                    Environment.Exit(-1);
                }
                ExtractScsFile(path, start);
            }
        }

        private static void ExtractAllInDirectory(string path, string start)
        {
            if (!Directory.Exists(path))
            {
                Console.Error.WriteLine($"\"{path}\" is not a directory or does not exist.");
                Environment.Exit(-1);
            }
            foreach (var scsPath in Directory.EnumerateFiles(path, "*.scs"))
            {
                ExtractScsFile(scsPath, start);
            }
        }

        private static void ExtractScsFile(string scsPath, string start)
        {
            var reader = HashFsReader.Open(scsPath);

            switch (reader.EntryExists(start))
            {
                case EntryType.Directory:
                    Extract(reader, start, Path.GetFileName(scsPath));
                    break;
                case EntryType.File:
                    ExtractSingleFile(start, reader);
                    break;
                case EntryType.NotFound:
                    break;
            }
        }

        private static void Extract(HashFsReader r, string directory, string scsName)
        {
            Console.Out.WriteLine($"Extracting {scsName}{directory} ...");

            var (subdirs, files) = r.GetDirectoryListing(directory); 
            Directory.CreateDirectory(Path.Combine(destination, directory[1..]));
            ExtractFiles(r, files);

            foreach (var subdir in subdirs)
                Extract(r, subdir, scsName);
        }

        private static void ExtractFiles(HashFsReader r, List<string> files)
        {
            foreach (var file in files)
            {
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
