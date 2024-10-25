using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TruckLib.HashFs;
using Mono.Options;
using Ionic.Zlib;
using System.Diagnostics;
using static Extractor.Util;
using System.Reflection.PortableExecutable;
using System.Threading;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;

namespace Extractor
{
    class Program
    {
        const string Version = "2024-10-24";

        static bool launchedByExplorer = false;
        static string destination = "./extracted";
        static bool skipIfExists = false;
        static bool forceEntryTableAtEnd = false;
        static bool listEntries = false;
        static List<string> inputPaths;
        static bool extractAllInDir = false;
        static string[] startPaths = ["/"];
        static bool printHelp = false;
        static bool rawMode = false;
        static bool tree = false;
        static ushort? salt = null;

        static void Main(string[] args)
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

            var optionSet = ParseOptions(args);

            if (printHelp || args.Length == 0)
            {
                Console.WriteLine($"Extractor {Version}\n");
                Console.WriteLine("Usage:\n  extractor path... [options]\n");
                Console.WriteLine("Options:");
                optionSet.WriteOptionDescriptions(Console.Out);
                PauseIfNecessary();
                return;
            }

            Extract();
            PauseIfNecessary();
        }

        private static OptionSet ParseOptions(string[] args)
        {
            var optionSet = new OptionSet()
            {
                { "a|all",
                    "Extract all .scs archives in the specified directory.",
                    x => { extractAllInDir = true; } },
                { "d=|dest=",
                    $"The output directory.\nDefault: {destination}",
                    x => { destination = x; } },
                { "list",
                    "[HashFS] Lists entries and exits.",
                    x => { listEntries = true; } },
                { "p=|partial=",
                    "Partial extraction, e.g.:\n" +
                    "-p=/locale\n" +
                    "-p=/def,/map\n" +
                    "-p=/def/world/road.sii",
                    x => { startPaths = x.Split(","); } },
                 { "P=|paths=",
                    "Same as --partial, but expects a text file containing paths to extract, " +
                    "separated by newlines.",
                    x => { startPaths = LoadStartPathsFromFile(x); } },
                { "r|raw",
                    "[HashFS] Directly dumps the contained files with their hashed " +
                    "filenames rather than traversing the archive's directory tree.",
                    x => { rawMode = true; } },
                { "salt=",
                    "[HashFS] Ignores the salt in the archive header and uses this one instead.",
                    x => { salt = ushort.Parse(x); } },
                { "s|skip-existing",
                    "Don't overwrite existing files.",
                    x => { skipIfExists = true; } },
                { "table-at-end",
                    "[HashFS v1] Ignores what the archive header says and reads " +
                    "the entry table from the end of the file.",
                    x => { forceEntryTableAtEnd = true; } },
                { "tree",
                    "[HashFS] Prints the directory tree and exits. Can be combined with " +
                    "--partial, --paths, and --all.",
                    x => { tree = true; } },
                { "?|h|help",
                    $"Prints this message and exits.",
                    x => { printHelp = true; } },
            };
            inputPaths = optionSet.Parse(args);
            if (inputPaths.Count == 0)
            {
                inputPaths.Add(".");
            }
            return optionSet;
        }

        private static void Extract()
        {
            startPaths = startPaths.Select(x => x.StartsWith('/') ? x : $"/{x}").ToArray();

            var scsPaths = GetScsPathsFromArgs();
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
                PrintArchiveOpenedMessage(extractor, scsPath);

                if (listEntries)
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
                else if (tree)
                {
                    if (extractor is HashFsExtractor)
                    {
                        Tree.PrintTree((extractor as HashFsExtractor).Reader, startPaths);
                    }
                    else
                    {
                        Console.WriteLine("--tree can only be used with HashFS archives.");
                    }
                }
                else
                {
                    extractor.Extract(startPaths, destination);
                }
            }
        }

        private static void PrintArchiveOpenedMessage(Extractor extractor, string scsPath)
        {
            var scsName = Path.GetFileName(scsPath);
            Console.Write($"Opened {scsName}: ");

            switch (extractor)
            {
                case HashFsExtractor h:
                    Console.Write($"HashFS v{h.Reader.Version} archive, {h.Reader.Entries.Count} entries\n");
                    break;
                case ZipExtractor z:
                    Console.Write($"ZIP archive, {z.Entries.Count} entries\n");
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private static Extractor CreateExtractor(string scsPath)
        {
            Extractor extractor;

            // check if the file begins with "SCS#", the magic bytes of a HashFS file.
            // anything else is assumed to be a zip file because simply checking for "PK"
            // would miss zip files with invalid local file headers.
         
            char[] magic;
            using (var fs = new FileStream(scsPath, FileMode.Open, FileAccess.Read))
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
                extractor = new ZipExtractor(scsPath, !skipIfExists);
            }

            return extractor;
        }

        private static Extractor CreateHashFsExtractor(string scsPath)
        {
            if (rawMode)
            {
                return new HashFsRawExtractor(scsPath, !skipIfExists)
                {
                    ForceEntryTableAtEnd = forceEntryTableAtEnd,
                    Salt = salt,
                    PrintNotFoundMessage = !extractAllInDir,
                };
            }
            return new HashFsExtractor(scsPath, !skipIfExists)
            {
                ForceEntryTableAtEnd = forceEntryTableAtEnd,
                Salt = salt,
                PrintNotFoundMessage = !extractAllInDir,
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

        private static IEnumerable<string> GetScsPathsFromArgs()
        {
            List<string> scsPaths;
            if (extractAllInDir)
            {
                scsPaths = [];
                foreach (var inputPath in inputPaths)
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
                scsPaths = inputPaths;
            }
            return scsPaths.Distinct();
        }

        private static void PauseIfNecessary()
        {
            if (launchedByExplorer)
            {
                Console.WriteLine("Press any key to continue ...");
                Console.Read();
            }
        }

        private static string[] LoadStartPathsFromFile(string file)
        {
            if (!File.Exists(file))
            {
                Console.Error.WriteLine($"File {file} does not exist");
            }
            return File.ReadAllLines(file).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        }
    }
}
