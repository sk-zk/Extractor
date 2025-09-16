using Mono.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Extractor.PathUtils;

namespace Extractor
{
    internal class Options
    {
        public OptionSet OptionSet { get; init; }

        public IList<string> AdditionalStartPaths { get; set; }
        public bool ExtractAllInDir { get; set; } = false;
        public bool ForceEntryTableAtEnd { get; set; } = false;
        public List<string> InputPaths { get; set; }
        public bool ListAll { get; set; } = false;
        public bool ListEntries { get; set; } = false;
        public bool ListPaths { get; set; } = false;
        public IList<string> PathFilter { get; set; } = ["/"];
        public bool PrintHelp { get; set; } = false;
        public bool PrintTree { get; set; } = false;
        public bool SkipIfExists { get; set; } = false;
        public bool UseDeepExtractor { get; set; } = false;
        public bool UseRawExtractor { get; set; } = false;
        public string Destination { get; set; } = "./extracted";
        public ushort? Salt { get; set; } = null;

        public Options()
        {
            OptionSet = new OptionSet()
            {
                 { "additional=",
                    "[HashFS] When using --deep, specifies additional start paths to search. " +
                    "Expects a text file containing paths to extract, separated by line breaks.",
                    x => { AdditionalStartPaths = LoadPathsFromFile(x); } },
                { "a|all",
                    "Extract all .scs archives in the specified directory.",
                    x => { ExtractAllInDir = true; } },
                { "deep",
                    $"[HashFS] Scans contained files for paths before extracting. Use this option " +
                    $"to extract archives without a top level directory listing.",
                    x => { UseDeepExtractor = true; } },
                { "d=|dest=",
                    $"The output directory.\nDefault: {Destination}",
                    x => { Destination = x; } },
                { "list",
                    "Lists paths contained in the archive. Can be combined with --deep.",
                    x => { ListPaths = true; } },
                { "list-all",
                    "[HashFS] Lists all paths referenced by files in the archive, " +
                    "even if they are not contained in it. Implicitly activates --deep.",
                    x => { ListPaths = true; ListAll = true; UseDeepExtractor = true;  } },
                { "list-entries",
                    "[HashFS] Lists entries contained in the archive.",
                    x => { ListEntries = true; } },
                { "p=|partial=",
                    "Limits extraction to the comma-separated list of files and/or directories specified, e.g.:\n" +
                    "-p=/locale\n" +
                    "-p=/def,/map\n" +
                    "-p=/def/world/road.sii",
                    x => { PathFilter = x.Split(","); } },
                 { "P=|paths=",
                    "Same as --partial, but expects a text file containing paths to extract, " +
                    "separated by line breaks.",
                    x => { PathFilter = LoadPathsFromFile(x); } },
                { "r|raw",
                    "[HashFS] Directly dumps the contained files with their hashed " +
                    "filenames rather than traversing the archive's directory tree.",
                    x => { UseRawExtractor = true; } },
                { "salt=",
                    "[HashFS] Ignores the salt in the archive header and uses this one instead.",
                    x => { Salt = ushort.Parse(x); } },
                { "s|skip-existing",
                    "Don't overwrite existing files.",
                    x => { SkipIfExists = true; } },
                { "table-at-end",
                    "[HashFS v1] Ignores what the archive header says and reads " +
                    "the entry table from the end of the file.",
                    x => { ForceEntryTableAtEnd = true; } },
                { "tree",
                    "Prints the directory tree and exits. Can be combined with " +
                    "-deep, --partial, --paths,\nand --all.",
                    x => { PrintTree = true; } },
                { "?|h|help",
                    $"Prints this message and exits.",
                    x => { PrintHelp = true; } },
            };
        }

        public void Parse(string[] args)
        {
            InputPaths = OptionSet.Parse(args);
            if (InputPaths.Count == 0 && ExtractAllInDir)
            {
                InputPaths.Add(".");
            }
        }

        private static List<string> LoadPathsFromFile(string file)
        {
            if (!File.Exists(file))
            {
                Console.Error.WriteLine($"File {file} does not exist.");
                Environment.Exit(1);
            }

            List<string> paths = [];
            using var reader = new StreamReader(file);
            while (!reader.EndOfStream)
            {
                var path = reader.ReadLine();

                if (string.IsNullOrEmpty(path))
                    continue;

                EnsureHasInitialSlash(ref path);

                paths.Add(path);
            }

            return paths;
        }
    }
}
