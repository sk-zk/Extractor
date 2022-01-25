using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TruckLib.HashFs;

namespace Extractor
{
    class Program
    {
        static string outDir = "./extracted/";

        static void Main(string[] args)
        {
            if (args.Length == 0 || IsHelpArg(args[0]))
            {
                PrintUsage();
                return;
            }

            var input = args[0];

            if (args.Length > 1 && !args[1].StartsWith("-"))
                outDir = args[1];
            string start = GetStart(args);

            var reader = HashFsReader.Open(input);

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
            var outputPath = Path.Combine(outDir, path[1..]);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            Console.WriteLine($"Extracting {path} ...");
            reader.ExtractToFile(path, outputPath);
        }

        private static string GetStart(string[] args)
        {
            var start = "/";
            var (success, value) = TryGetParamValue(args, "-p");
            return success ? value : start;
        }

        private static (bool success, string value) TryGetParamValue(string[] args, string param)
        {
            var idx = Array.IndexOf(args, param);
            if (idx != -1 && args.Length > idx + 1)
            {
                return (true, args[idx + 1]);
            }
            return (false, null);
        }

        private static bool IsHelpArg(string arg)
        {
            string[] helpArgs = { "--help", "-help", "/help", "--h", "-h", "/h", "--?", "-?", "/?" };
            arg = arg.ToLowerInvariant();
            return helpArgs.Contains(arg);
        }

        private static void PrintUsage()
        {
            Console.WriteLine(@"Usage:
extractor scs_file [output_dir] [params]

Parameters:
  -p path    Only extract the given file or directory, 
                e.g. ""-p /map"".
");
        }

        private static void Extract(HashFsReader r, string dir)
        {
            Console.Out.WriteLine($"Extracting {dir} ...");

            var (subdirs, files) = r.GetDirectoryListing(dir); 
            Directory.CreateDirectory(Path.Combine(outDir, dir[1..]));
            ExtractFiles(r, files);

            foreach (var subdir in subdirs)
                Extract(r, subdir);
        }

        private static void ExtractFiles(HashFsReader r, List<string> files)
        {
            foreach (var file in files)
            {
                var path = Path.Combine(outDir, file[1..]);
                r.ExtractToFile(file, path);
            }
        }

        private static void PrintError(string msg)
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(msg);
            Console.ForegroundColor = prev;
        }

        
    }
}
