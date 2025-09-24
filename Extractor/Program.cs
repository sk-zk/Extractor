using Extractor.Deep;
using Extractor.Zip;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Extractor.PathUtils;

namespace Extractor
{
    class Program
    {
        private const string Version = "2025-09-18";
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

            // Configure sanitization behavior
            PathUtils.LegacyReplaceMode = opt.LegacySanitize;

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

            if (opt.Benchmark)
            {
                RunBenchmark(scsPaths);
                return;
            }

            if (opt.UseDeepExtractor && scsPaths.Length > 1)
            {
                DoMultiDeepExtraction(scsPaths);
                return;
            }

            foreach (var scsPath in scsPaths)
            {
                var swOpen = Stopwatch.StartNew();
                var extractor = CreateExtractor(scsPath);
                swOpen.Stop();
                if (extractor is null)
                    continue;

                extractor.DryRun = opt.DryRun;
                extractor.PrintTimesEnabled = opt.Times;
                if (extractor is HashFsDeepExtractor hdeep)
                {
                    hdeep.SingleThreadedPathSearch = opt.SingleThread;
                    if (!opt.SingleThread)
                    {
                        hdeep.ReaderPoolSize = opt.IoReaders > 0 ? opt.IoReaders : Math.Max(1, Environment.ProcessorCount);
                    }
                }
                if (opt.Times)
                {
                    extractor.OpenTime = swOpen.Elapsed;
                }

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
                    extractor.PrintPaths(opt.PathFilter, opt.ListAll);
                }
                else if (opt.PrintTree)
                {
                    if (opt.UseRawExtractor)
                    {
                        Console.Error.WriteLine("--tree and --raw cannot be combined.");
                    }
                    else
                    {
                        var trees = extractor.GetDirectoryTree(opt.PathFilter);
                        var scsName = Path.GetFileName(extractor.ScsPath);
                        Tree.TreePrinter.Print(trees, scsName);
                    }
                }
                else
                {
                    if (extractor is HashFsDeepExtractor hde)
                    {
                        if (opt.Times)
                        {
                            Console.WriteLine("Searching for paths ...");
                            var swSearch = Stopwatch.StartNew();
                            var (found, _) = hde.FindPaths();
                            extractor.SearchTime = swSearch.Elapsed;
                            var foundFiles = found.Order().ToArray();
                            var swExtract = Stopwatch.StartNew();
                            hde.Extract(foundFiles, opt.PathFilter, GetDestination(scsPath), false);
                            extractor.ExtractTime = swExtract.Elapsed;
                        }
                        else
                        {
                            hde.Extract(opt.PathFilter, GetDestination(scsPath));
                        }
                    }
                    else
                    {
                        if (opt.Times)
                        {
                            var swExtract = Stopwatch.StartNew();
                            extractor.Extract(opt.PathFilter, GetDestination(scsPath));
                            extractor.ExtractTime = swExtract.Elapsed;
                        }
                        else
                        {
                            extractor.Extract(opt.PathFilter, GetDestination(scsPath));
                        }
                    }
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
                if (IsHashFs(scsPath))
                {
                    extractor = CreateHashFsExtractor(scsPath);
                }
                else
                {
                    extractor = new ZipExtractor(scsPath, !opt.SkipIfExists);
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

        private static void DoMultiDeepExtraction(string[] scsPaths)
        {
            // If you're reading this ... maybe don't.

            List<Extractor> extractors = [];
            var openTimes = new Dictionary<string, TimeSpan>();
            foreach (var scsPath in scsPaths)
            {
                var swOpen = Stopwatch.StartNew();
                var extractor = CreateExtractor(scsPath);
                swOpen.Stop();
                if (extractor is not null)
                {
                    extractor.DryRun = opt.DryRun;
                    extractor.PrintTimesEnabled = opt.Times;
                    if (extractor is HashFsDeepExtractor hd)
                    {
                        hd.SingleThreadedPathSearch = opt.SingleThread;
                        if (!opt.SingleThread)
                        {
                            hd.ReaderPoolSize = opt.IoReaders > 0 ? opt.IoReaders : Math.Max(1, Environment.ProcessorCount);
                        }
                    }
                    extractors.Add(extractor);
                    openTimes[extractor.ScsPath] = swOpen.Elapsed;
                    if (opt.Times)
                    {
                        extractor.OpenTime = swOpen.Elapsed;
                    }                    
                }
            }

            var bag = new System.Collections.Concurrent.ConcurrentBag<string>();
            Parallel.ForEach(extractors, extractor =>
            {
                Console.WriteLine($"Searching for paths in {Path.GetFileName(extractor.ScsPath)} ...");
                if (extractor is HashFsDeepExtractor hashFs)
                {
                    var swSearch = Stopwatch.StartNew();
                    var (found, referenced) = hashFs.FindPaths();
                    swSearch.Stop();
                    if (opt.Times)
                    {
                        extractor.SearchTime = swSearch.Elapsed;
                    }
                    foreach (var p in found)
                    {
                        bag.Add(p);
                    }
                    foreach (var p in referenced)
                    {
                        bag.Add(p);
                    }
                }
                else if (extractor is ZipExtractor zip)
                {
                    var finder = new ZipPathFinder(zip.Reader);
                    var swSearch = Stopwatch.StartNew();
                    finder.Find();
                    swSearch.Stop();
                    if (opt.Times)
                    {
                        extractor.SearchTime = swSearch.Elapsed;
                    }
                    foreach (var p in finder.ReferencedFiles)
                    {
                        bag.Add(p);
                    }
                    foreach (var p in zip.Reader.Entries.Keys.Select(p => '/' + p))
                    {
                        bag.Add(p);
                    }
                }
                else
                {
                    throw new ArgumentException("Unhandled extractor type");
                }
            });

            var everything = new HashSet<string>(bag);

            var extractTimes = new Dictionary<string, TimeSpan>();
            foreach (var extractor in extractors)
            {
                var existing = everything.Where(extractor.FileSystem.FileExists);

                if (opt.ListPaths)
                {
                    foreach (var path in existing.Order())
                    {
                        Console.WriteLine(ReplaceControlChars(path));
                    }
                }
                else if (opt.PrintTree)
                {
                    var trees = opt.PathFilter
                        .Select(startPath => PathListToTree(startPath, existing))
                        .ToList();
                    Tree.TreePrinter.Print(trees, Path.GetFileName(extractor.ScsPath));
                }
                else
                {
                    if (extractor is HashFsDeepExtractor hashFs)
                    {
                        var swExtract = Stopwatch.StartNew();
                        hashFs.Extract(existing.ToArray(), opt.PathFilter, GetDestination(extractor.ScsPath), true);
                        extractTimes[extractor.ScsPath] = swExtract.Elapsed;
                        if (opt.Times)
                        {
                            extractor.ExtractTime = swExtract.Elapsed;
                        }
                    }
                    else
                    {
                        var swExtract = Stopwatch.StartNew();
                        extractor.Extract(opt.PathFilter, GetDestination(extractor.ScsPath));
                        extractTimes[extractor.ScsPath] = swExtract.Elapsed;
                        if (opt.Times)
                        {
                            extractor.ExtractTime = swExtract.Elapsed;
                        }
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

        private static bool IsHashFs(string scsPath)
        {
            try
            {
                using var fs = File.OpenRead(scsPath);
                using var r = new BinaryReader(fs, Encoding.ASCII);
                var magic = r.ReadChars(4);
                return magic.SequenceEqual(['S','C','S','#']);
            }
            catch { return false; }
        }

        private static void RunBenchmark(string[] scsPaths)
        {
            foreach (var scsPath in scsPaths)
            {
                if (!IsHashFs(scsPath))
                {
                    Console.Error.WriteLine($"Benchmark: skipping {Path.GetFileName(scsPath)} (not a HashFS archive)");
                    continue;
                }

                Console.WriteLine($"Benchmarking {Path.GetFileName(scsPath)} (deep scan, dry-run) ...");

                HashFsDeepExtractor MakeExtractor(bool singleThread)
                {
                    var x = new HashFsDeepExtractor(scsPath, overwrite: !opt.SkipIfExists, opt.Salt)
                    {
                        ForceEntryTableAtEnd = opt.ForceEntryTableAtEnd,
                        PrintNotFoundMessage = !opt.ExtractAllInDir,
                        AdditionalStartPaths = opt.AdditionalStartPaths,
                        SingleThreadedPathSearch = singleThread,
                        DryRun = true,
                    };
                    if (!singleThread && opt.IoReaders > 0)
                    {
                        x.ReaderPoolSize = Math.Max(1, opt.IoReaders);
                    }
                    return x;
                }

                // Single-thread pass
                var single = MakeExtractor(true);
                var swSingle = Stopwatch.StartNew();
                var (foundSingle, _) = single.FindPaths();
                swSingle.Stop();
                single.SearchTime = swSingle.Elapsed; // already set inside, but ensure measured wall time

                // Multi-thread pass
                var multi = MakeExtractor(false);
                var swMulti = Stopwatch.StartNew();
                var (foundMulti, _) = multi.FindPaths();
                swMulti.Stop();
                multi.SearchTime = swMulti.Elapsed;

                void PrintBench(string title, HashFsDeepExtractor e, int found)
                {
                    Console.WriteLine(
                        $"  {title}: search={e.SearchTime?.TotalMilliseconds:F0}ms " +
                        $"(decomp={e.SearchExtractTime?.TotalMilliseconds:F0}ms, " +
                        $"decomp_wall={e.SearchExtractWallTime?.TotalMilliseconds:F0}ms, " +
                        $"parse={e.SearchParseTime?.TotalMilliseconds:F0}ms, " +
                        $"files={e.SearchFilesParsed?.ToString() ?? "-"}, " +
                        $"unique={e.SearchUniqueFilesParsed?.ToString() ?? "-"}, " +
                        $"bytes={e.SearchBytesInflated?.ToString() ?? "-"}), " +
                        $"found={found}");
                }

                PrintBench("single-thread", single, foundSingle.Count);
                PrintBench("multi-thread", multi, foundMulti.Count);

                if (single.SearchTime.HasValue && multi.SearchTime.HasValue)
                {
                    var speedup = single.SearchTime.Value.TotalMilliseconds / Math.Max(1, multi.SearchTime.Value.TotalMilliseconds);
                    Console.WriteLine($"  speedup: x{speedup:F2}");
                }

                // Dispose
                single.Dispose();
                multi.Dispose();
            }
        }

        private static Extractor CreateHashFsExtractor(string scsPath)
        {
            if (opt.UseRawExtractor)
            {
                return new HashFsRawExtractor(scsPath, !opt.SkipIfExists, opt.Salt)
                {
                    ForceEntryTableAtEnd = opt.ForceEntryTableAtEnd,
                    PrintNotFoundMessage = !opt.ExtractAllInDir,
                };
            }
            else if (opt.UseDeepExtractor)
            {
                return new HashFsDeepExtractor(scsPath, !opt.SkipIfExists, opt.Salt)
                {
                    ForceEntryTableAtEnd = opt.ForceEntryTableAtEnd,
                    PrintNotFoundMessage = !opt.ExtractAllInDir,
                    AdditionalStartPaths = opt.AdditionalStartPaths,
                };
            }
            return new HashFsExtractor(scsPath, !opt.SkipIfExists, opt.Salt)
            {
                ForceEntryTableAtEnd = opt.ForceEntryTableAtEnd,
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


