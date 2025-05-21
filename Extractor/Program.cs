using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using static Extractor.PathUtils;
using Extractor.Deep;
using Extractor.Zip;

namespace Extractor
{
    class Program
    {
        const string Version = "2025-05-03";
        private static bool launchedByExplorer = false;
        private static Options opt;

        public static void Main(string[] args)
        {
            if (OperatingSystem.IsWindows())
            {
                // Detect whether the extractor was launched by Explorer to pause at the end
                // so that people who just want to drag and drop a file onto it can read the
                // error message if one occurs.
                // This works for both conhost and Windows Terminal.
                launchedByExplorer = !Debugger.IsAttached &&
                    Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Console.Title)) ==
                    Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            }

            Console.OutputEncoding = Encoding.UTF8;

            opt = new Options();
            opt.Parse(args);

            if (opt.PrintHelp || args.Length == 0)
            {
                Console.WriteLine($"Extractor {Version}\n");
                Console.WriteLine("Usage:\n  extractor path... [options]\n");
                Console.WriteLine("Options:");
                opt.OptionSet.WriteOptionDescriptions(Console.Out);
                PauseIfNecessary();
                return;
            }
            if (opt.InputPaths.Count == 0)
            {
                Console.WriteLine("No input paths specified.");
                PauseIfNecessary();
                return;
            }

            Run();
            PauseIfNecessary();
        }

        private static void Run()
        {
            var scsPaths = GetScsPathsFromArgs();
            if (scsPaths.Length == 0)
            {
                Console.WriteLine("No .scs files were found.");
                return;
            }

            foreach (var scsPath in scsPaths)
            {
                if (!File.Exists(scsPath))
                {
                    Console.Error.WriteLine($"{scsPath} is not a file or does not exist.");
                    continue;
                }

                Extractor extractor;
                try
                {
                    extractor = CreateExtractor(scsPath);
                }
                catch (InvalidDataException)
                {
                    Console.Error.WriteLine($"Unable to open {scsPath}: Not a HashFS or ZIP archive");
                    continue;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Unable to open {scsPath}: {ex.Message}");
                    continue;
                }
                extractor.PrintContentSummary();

                if (opt.ListEntries)
                {
                    if (extractor is HashFsExtractor)
                    {
                        ListEntries(extractor);
                    }
                    else
                    {
                        Console.WriteLine("--list can only be used with HashFS archives.");
                    }
                }
                else if (opt.ListPaths)
                {
                    extractor.PrintPaths(opt.PathFilter, opt.ListAll);
                }
                else if (opt.PrintTree)
                {
                    if (opt.UseRawExtractor)
                    {
                        Console.WriteLine("--tree and --raw cannot be combined.");
                    }
                    else
                    {
                        var trees = extractor.GetDirectoryTree(opt.PathFilter);
                        var scsName = Path.GetFileName(scsPath);
                        Tree.TreePrinter.Print(trees, scsName);
                    }
                }
                else
                {
                    extractor.Extract(opt.PathFilter, opt.Destination);
                    extractor.PrintExtractionResult();
                }
            }
        }

        private static Extractor CreateExtractor(string scsPath)
        {
            Extractor extractor;

            // check if the file begins with "SCS#", the magic bytes of a HashFS file.
            // anything else is assumed to be a zip file because simply checking for "PK"
            // would miss zip files with invalid local file headers.
         
            char[] magic;
            using (var fs = File.OpenRead(scsPath))
            using (var r = new BinaryReader(fs, Encoding.ASCII))
            {
                magic = r.ReadChars(4);
            }

            if (magic.SequenceEqual(['S', 'C', 'S', '#']))
            {
                extractor = CreateHashFsExtractor(scsPath);
            }
            else
            {
                extractor = new ZipExtractor(scsPath, !opt.SkipIfExists);
            }

            return extractor;
        }

        private static Extractor CreateHashFsExtractor(string scsPath)
        {
            if (opt.UseRawExtractor)
            {
                return new HashFsRawExtractor(scsPath, !opt.SkipIfExists)
                {
                    ForceEntryTableAtEnd = opt.ForceEntryTableAtEnd,
                    Salt = opt.Salt,
                    PrintNotFoundMessage = !opt.ExtractAllInDir,
                };
            }
            else if (opt.UseDeepExtractor)
            {
                return new HashFsDeepExtractor(scsPath, !opt.SkipIfExists)
                {
                    ForceEntryTableAtEnd = opt.ForceEntryTableAtEnd,
                    Salt = opt.Salt,
                    PrintNotFoundMessage = !opt.ExtractAllInDir,
                    AdditionalStartPaths = opt.AdditionalStartPaths,
                };
            }
            return new HashFsExtractor(scsPath, !opt.SkipIfExists)
            {
                ForceEntryTableAtEnd = opt.ForceEntryTableAtEnd,
                Salt = opt.Salt,
                PrintNotFoundMessage = !opt.ExtractAllInDir,
            };
        }

        private static void ListEntries(Extractor extractor)
        {
            if (extractor is HashFsExtractor hExt)
            {
                Console.WriteLine($"  {"Offset",-10}  {"Hash",-16}  {"Cmp. Size",-10}  {"Uncmp.Size",-10}");
                foreach (var (_, entry) in hExt.Reader.Entries)
                {
                    Console.WriteLine($"{(entry.IsDirectory ? "*" : " ")} " +
                        $"{entry.Offset,10}  {entry.Hash,16:x}  {entry.CompressedSize,10}  {entry.Size,10}");
                }
            }
        }

        private static string[] GetScsPathsFromArgs()
        {
            List<string> scsPaths;
            if (opt.ExtractAllInDir)
            {
                scsPaths = [];
                foreach (var inputPath in opt.InputPaths)
                {
                    if (!Directory.Exists(inputPath))
                    {
                        Console.Error.WriteLine($"{inputPath} is not a directory or does not exist.");
                        continue;
                    }
                    scsPaths.AddRange(GetAllScsFiles(inputPath));
                }
            }
            else
            {
                scsPaths = opt.InputPaths;
            }
            return scsPaths.Distinct().ToArray();
        }

        private static void PauseIfNecessary()
        {
            if (launchedByExplorer)
            {
                Console.WriteLine("Press any key to continue ...");
                Console.Read();
            }
        }
    }
}
