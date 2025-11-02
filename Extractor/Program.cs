using Extractor.Deep;
using Extractor.Zip;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using static Extractor.PathUtils;

namespace Extractor
{
    class Program
    {
        private const string Version = "2025-11-02";
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
            if (opt.InputPaths is null || opt.InputPaths.Count == 0)
            {
                Console.Error.WriteLine("No input paths specified.");
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
                Console.Error.WriteLine("No .scs files were found.");
                return;
            }

            if (opt.UseDeepExtractor && scsPaths.Length > 1)
            {
                DoMultiDeepExtraction(scsPaths);
                return;
            }

            foreach (var scsPath in scsPaths)
            {
                var extractor = CreateExtractor(scsPath);
                if (extractor is null)
                    continue;

                if (opt.ListEntries)
                {
                    if (extractor is HashFsExtractor)
                    {
                        ListEntries(extractor);
                    }
                    else
                    {
                        Console.Error.WriteLine("--list can only be used with HashFS archives.");
                    }
                }
                else if (opt.ListPaths)
                {
                    extractor.PrintPaths(opt.ListAll);
                }
                else if (opt.PrintTree)
                {
                    if (opt.UseRawExtractor)
                    {
                        Console.Error.WriteLine("--tree and --raw cannot be combined.");
                    }
                    else
                    {
                        var trees = extractor.GetDirectoryTree();
                        var scsName = Path.GetFileName(extractor.ScsPath);
                        Tree.TreePrinter.Print(trees, scsName);
                    }
                }
                else
                {
                    extractor.Extract(GetDestination(scsPath));
                    extractor.PrintExtractionResult();
                }
            }
        }

        private static string GetDestination(string scsPath)
        {
            if (opt.Separate)
            {
                var scsName = Path.GetFileNameWithoutExtension(scsPath);
                var destination = Path.Combine(opt.Destination, scsName);
                return destination;
            }
            return opt.Destination;
        }

        private static Extractor CreateExtractor(string scsPath)
        {
            if (!File.Exists(scsPath))
            {
                Console.Error.WriteLine($"{scsPath} is not a file or does not exist.");
                return null;
            }

            Extractor extractor = null;
            try
            {
                // Check if the file begins with "SCS#", the magic bytes of a HashFS file.
                // Anything else is assumed to be a ZIP file because simply checking for "PK"
                // would miss ZIP files with invalid local file headers.
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
                    extractor = new ZipExtractor(scsPath, opt);
                }
            }
            catch (InvalidDataException)
            {
                Console.Error.WriteLine($"Unable to open {scsPath}: Not a HashFS or ZIP archive");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unable to open {scsPath}: {ex.Message}");
            }
            return extractor;
        }

        private static Extractor CreateHashFsExtractor(string scsPath)
        {
            if (opt.UseRawExtractor)
                return new HashFsRawExtractor(scsPath, opt);
            else if (opt.UseDeepExtractor)
                return new HashFsDeepExtractor(scsPath, opt);
            else
                return new HashFsExtractor(scsPath, opt);
        }

        private static void DoMultiDeepExtraction(string[] scsPaths)
        {
            // If you're reading this ... maybe don't.

            List<Extractor> extractors = [];
            foreach (var scsPath in scsPaths)
            {
                var extractor = CreateExtractor(scsPath);
                if (extractor is not null)
                    extractors.Add(extractor);
            }

            HashSet<string> everything = [];
            foreach (var extractor in extractors)
            {
                Console.WriteLine($"Searching for paths in {Path.GetFileName(extractor.ScsPath)} ...");
                if (extractor is HashFsDeepExtractor hashFs)
                {
                    var (found, referenced) = hashFs.FindPaths();
                    everything.UnionWith(found);
                    everything.UnionWith(referenced);
                }
                else if (extractor is ZipExtractor zip)
                {
                    var finder = new ZipPathFinder(zip.Reader);
                    finder.Find();
                    var paths = finder.ReferencedFiles
                        .Union(zip.Reader.Entries.Keys.Select(p => '/' + p));
                    everything.UnionWith(paths);
                }
                else
                {
                    throw new ArgumentException("Unhandled extractor type");
                }
            }

            foreach (var extractor in extractors)
            {
                var existing = everything.Where(extractor.FileSystem.FileExists);

                if (opt.ListPaths)
                {
                    ConsoleUtils.PrintPathsMatchingFilters(existing.Order(), opt.StartPaths, opt.Filters);
                }
                else if (opt.PrintTree)
                {
                    var trees = opt.StartPaths
                        .Select(startPath => PathListToTree(startPath, existing))
                        .ToList();
                    Tree.TreePrinter.Print(trees, Path.GetFileName(extractor.ScsPath));
                }
                else
                {
                    if (extractor is HashFsDeepExtractor deep)
                    {
                        deep.Extract(existing.ToArray(), GetDestination(extractor.ScsPath), true);
                    }
                    else
                    {
                        extractor.Extract(GetDestination(extractor.ScsPath));
                    }
                }
            }

            if (!opt.ListPaths && !opt.PrintTree)
            {
                foreach (var extractor in extractors)
                {
                    Console.Write($"{Path.GetFileName(extractor.ScsPath)}: ");
                    extractor.PrintExtractionResult();
                }
            }
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
