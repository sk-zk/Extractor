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

namespace Extractor
{
    class Program
    {
        const string Version = "2024-09-02";

        static bool launchedByExplorer = false;
        static string destination = "./extracted/";
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

            var p = new OptionSet()
            {
                { "a|all",
                    "Extracts all .scs archives in the specified directory.",
                    x => { extractAllInDir = true; } },
                { "d=|dest=",
                    $"The output directory.\nDefault: {destination}",
                    x => { destination = x; } },
                { "list",
                    "Lists entries and exits.",
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
                    "Directly dumps the contained files with their hashed filenames rather than " +
                    "traversing the archive's directory tree.",
                    x => { rawMode = true; } },
                { "salt=",
                    "Ignores the salt in the archive header and uses this one instead.",
                    x => { salt = ushort.Parse(x); } },
                { "s|skip-existing",
                    "Don't overwrite existing files.",
                    x => { skipIfExists = true; } },
                { "table-at-end",
                    "[HashFS v1 only] Ignores what the archive header says and reads " +
                    "the entry table from the end of the file.",
                    x => { forceEntryTableAtEnd = true; } },
                { "tree",
                    "Prints the directory tree and exits. Can be combined with --partial, " +
                    "--paths, and --all.",
                    x => { tree = true; } },
                { "?|h|help",
                    $"Prints this message and exits.",
                    x => { printHelp = true; } },
            };
            inputPaths = p.Parse(args);
            if (inputPaths.Count == 0)
            {
                inputPaths.Add(".");
            }

            if (printHelp || args.Length == 0)
            {
                Console.WriteLine($"Extractor {Version}\n");
                Console.WriteLine("Usage:\n  extractor path... [options]\n");
                Console.WriteLine("Options:");
                p.WriteOptionDescriptions(Console.Out);
                PauseIfNecessary();
                return;
            }

            Extract();
            PauseIfNecessary();
        }

        private static void Extract()
        {
            startPaths = startPaths.Select(x => x.StartsWith('/') ? x: $"/{x}").ToArray();

            var scsPaths = GetScsPathsFromArgs();
            foreach (var scsPath in scsPaths)
            {
                if (!File.Exists(scsPath))
                {
                    Console.Error.WriteLine($"{scsPath} is not a file or does not exist.");
                    continue;
                }

                if (listEntries)
                {
                    ListEntries(scsPath);
                    continue;
                }

                if (tree)
                {
                    Tree.PrintTree(scsPath, startPaths, forceEntryTableAtEnd);
                    continue;
                }

                if (rawMode)
                {
                    ExtractRaw(scsPath);
                }
                else
                {
                    ExtractScs(scsPath, startPaths, !extractAllInDir);
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
                Console.ReadLine();
            }
        }

        private static void ListEntries(string scsPath)
        {
            using var reader = HashFsReader.Open(scsPath, forceEntryTableAtEnd);
            Console.WriteLine($"  {"Offset",-10}  {"Hash",-16}  {"Cmp. Size",-10}  {"Uncmp.Size",-10}");
            foreach (var (_, entry) in reader.Entries)
            {
                Console.WriteLine($"{(entry.IsDirectory ? "*" : " ")} " +
                    $"{entry.Offset,10}  {entry.Hash,16:x}  {entry.CompressedSize,10}  {entry.Size,10}");
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

        private static void ExtractScs(string scsPath, string[] startPaths, 
            bool printNotFoundMessage = true)
        {
            IHashFsReader reader;
            try
            {
                reader = HashFsReader.Open(scsPath, forceEntryTableAtEnd);
            }
            catch (InvalidDataException idex)
            {
                Console.Error.WriteLine($"Unable to open {scsPath}: {idex.Message}");
                return;
            }

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
                        // TODO make sure this is actually a file
                        // and not a directory falsely labeled as one
                        var startPathWithoutSlash = startPath.StartsWith('/') ? startPath[1..] : startPath;
                        var outputPath = Path.Combine(destination, SanitizePath(startPathWithoutSlash));
                        Console.Out.WriteLine($"Extracting {ReplaceControlChars(startPath)} ...");
                        ExtractToFile(startPathWithoutSlash, outputPath, 
                            () => reader.ExtractToFile(startPathWithoutSlash, outputPath));
                        break;
                    case EntryType.NotFound:
                        if (startPath == "/")
                        {
                            Console.Error.WriteLine("Top level directory is missing; " +
                                "try a partial extraction or use --raw to dump entries");
                        }
                        else if (printNotFoundMessage)
                        {
                            Console.Error.WriteLine($"File or directory listing " +
                                $"{ReplaceControlChars(startPath)} does not exist");
                        }
                        break;
                }
            }

            reader?.Dispose();
        }

        private static void ExtractRaw(string scsPath)
        {
            var scsName = Path.GetFileName(scsPath);
            Console.Out.WriteLine($"Extracting {scsName} ...");
            var outputDir = Path.Combine(destination, scsName);
            Directory.CreateDirectory(outputDir);

            IHashFsReader reader;
            try
            {
                reader = HashFsReader.Open(scsPath, forceEntryTableAtEnd);
            }
            catch (InvalidDataException idex)
            {
                Console.Error.WriteLine($"Unable to open {scsPath}: {idex.Message}");
                return;
            }

            if (salt is not null)
            {
                reader.Salt = salt.Value;
            }

            var seenOffsets = new HashSet<ulong>();

            foreach (var (key, entry) in reader.Entries)
            {
                if (seenOffsets.Contains(entry.Offset)) continue;
                seenOffsets.Add(entry.Offset);

                // subdirectory listings are useless because the file names are relative
                if (entry.IsDirectory) continue;

                var outputPath = Path.Combine(outputDir, key.ToString("x"));
                ExtractToFile(key.ToString("x"), outputPath, () => reader.ExtractToFile(entry, "", outputPath));
            }

            reader?.Dispose();
        }

        private static void ExtractDirectory(IHashFsReader reader, string directory, string scsName)
        {
            Console.Out.WriteLine($"Extracting {scsName}{ReplaceControlChars(directory)} ...");

            var (subdirs, files) = reader.GetDirectoryListing(directory);
            Directory.CreateDirectory(Path.Combine(destination, directory[1..]));
            ExtractFiles(reader, files);
            ExtractSubdirectories(reader, subdirs, scsName);
        }

        private static void ExtractSubdirectories(IHashFsReader reader, List<string> subdirs, string scsName)
        {
            foreach (var subdir in subdirs)
            {
                var type = reader.EntryExists(subdir);
                switch (type)
                {
                    case EntryType.Directory:
                        ExtractDirectory(reader, subdir, scsName);
                        break;
                    case EntryType.File:
                        // the subdir list contains a path which has not been
                        // marked as a directory. it might be one regardless though,
                        // so let's just attempt to extract it anyway
                        var e = reader.GetEntry(subdir[..^1]);
                        e.IsDirectory = true;
                        ExtractDirectory(reader, subdir[..^1], scsName);
                        break;
                    case EntryType.NotFound:
                        Console.Error.WriteLine($"Directory {ReplaceControlChars(subdir)} is referenced" +
                            $" in a directory listing but could not be found in the archive");
                        continue;
                }
            }
        }

        private static void ExtractFiles(IHashFsReader reader, List<string> files)
        {
            foreach (var file in files)
            {
                var type = reader.EntryExists(file);
                switch (type)
                {
                    case EntryType.NotFound:
                        Console.Error.WriteLine($"File {ReplaceControlChars(file)} is referenced in" +
                            $" a directory listing but could not be found in the archive");
                        continue;
                    case EntryType.Directory:
                        // usually safe to ignore because it just points to the directory itself again
                        continue;
                }

                // The directory listing of core.scs only lists itself, but as a file, breaking everything
                if (file == "/") continue;

                var outputPath = Path.Combine(destination, SanitizePath(file[1..]));
                ExtractToFile(file, outputPath, () => reader.ExtractToFile(file, outputPath));
            }
        }

        private static void ExtractToFile(string archivePath, string outputPath, Action extractToFileCall)
        {
            if (skipIfExists && File.Exists(outputPath)) return;

            if (archivePath.StartsWith('/'))
            {
                archivePath = archivePath[1..];
            }

            try
            {
                extractToFileCall();
            }
            catch (ZlibException zlex)
            {
                Console.Error.WriteLine($"Unable to extract {ReplaceControlChars(archivePath)}:");
                Console.Error.WriteLine(zlex.Message);
            }
            catch (InvalidDataException idex)
            {
                Console.Error.WriteLine($"Unable to extract {ReplaceControlChars(archivePath)}:");
                Console.Error.WriteLine(idex.Message);
            }
            catch (AggregateException agex)
            {
                Console.Error.WriteLine($"Unable to extract {ReplaceControlChars(archivePath)}:");
                Console.Error.WriteLine(agex.ToString());
            }
            catch (IOException ioex)
            {
                Console.Error.WriteLine($"Unable to extract {ReplaceControlChars(archivePath)}:");
                Console.Error.WriteLine(ioex.Message);
            }
        }
    }
}
