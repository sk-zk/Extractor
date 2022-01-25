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
            string path = "";
            bool all = false;
            string start = "/";
            var p = new OptionSet()
            {
                { "<>",
                    "Path",
                    x => { path = x; } },
                { "d=",
                    $"The output directory.\nDefault: {destination}",
                    x => { destination = x; } },
                { "p=",
                    $"Partial extraction, e.g. \"-p /map\".",
                    x => { start = x; } },

            };
            if (args.Length == 0)
            {
                Console.WriteLine("extractor path [options]\n");
                Console.WriteLine("Options:");
                p.WriteOptionDescriptions(Console.Out);
                Environment.Exit(-1);
            }
            p.Parse(args);

            var reader = HashFsReader.Open(path);

            switch (reader.EntryExists(start))
            {
                case EntryType.Directory:
                    Extract(reader, start);
                    break;
                case EntryType.File:
                    ExtractSingleFile(start, reader);
                    break;
                case EntryType.NotFound:
                    Console.Error.WriteLine("The specified path does not exist.");
                    break;
            }
        }

        private static void ExtractSingleFile(string path, HashFsReader reader)
        {
            var outputPath = Path.Combine(destination, path[1..]);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            Console.WriteLine($"Extracting {path} ...");
            reader.ExtractToFile(path, outputPath);
        }

        private static void Extract(HashFsReader r, string dir)
        {
            Console.Out.WriteLine($"Extracting {dir} ...");

            var (subdirs, files) = r.GetDirectoryListing(dir); 
            Directory.CreateDirectory(Path.Combine(destination, dir[1..]));
            ExtractFiles(r, files);

            foreach (var subdir in subdirs)
                Extract(r, subdir);
        }

        private static void ExtractFiles(HashFsReader r, List<string> files)
        {
            foreach (var file in files)
            {
                var path = Path.Combine(destination, file[1..]);
                r.ExtractToFile(file, path);
            }
        }

    }
}
