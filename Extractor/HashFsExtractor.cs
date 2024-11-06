using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TruckLib.HashFs;
using static Extractor.PathUtils;
using static Extractor.ConsoleUtils;
using TruckLib.Sii;

namespace Extractor
{
    /// <summary>
    /// A HashFS extractor which wraps TruckLib's HashFsReader.
    /// </summary>
    public class HashFsExtractor : Extractor
    {
        /// <summary>
        /// The underlying HashFsReader.
        /// </summary>
        public IHashFsReader Reader { get; private set; }

        /// <summary>
        /// Overrides the salt found in the header with this one.
        /// </summary>
        /// <remarks>Solves #1.</remarks>
        public ushort? Salt { get; set; } = null;

        /// <summary>
        /// Gets or sets whether the entry table should be read from the end of the file
        /// regardless of where the header says it is located.
        /// </summary>
        /// <remarks>Solves #1.</remarks>
        public bool ForceEntryTableAtEnd { get; set; } = false;

        /// <summary>
        /// Gets or sets whether a "not found" message should be printed
        /// if a start path does not exist in the archive.
        /// </summary>
        public bool PrintNotFoundMessage { get; set; } = true;

        /// <summary>
        /// The number of files which have been extracted successfully.
        /// </summary>
        protected int extracted;

        /// <summary>
        /// The number of files which have been skipped because the output path
        /// already exists and <c>--skip-existing</c> was passed.
        /// </summary>
        protected int skipped;

        /// <summary>
        /// The number of files which failed to extract.
        /// </summary>
        protected int failed;

        /// <summary>
        /// The number of paths passed by the user which were not found in the archive.
        /// </summary>
        protected int notFound;

        /// <summary>
        /// The number of files whose paths had to be changed because they contained
        /// invalid characters.
        /// </summary>
        protected int renamed;

        public HashFsExtractor(string scsPath, bool overwrite) : base(scsPath, overwrite)
        {
            Reader = HashFsReader.Open(scsPath, ForceEntryTableAtEnd);

            extracted = 0;
            skipped = 0;
            failed = 0;
            notFound = 0;
            renamed = 0;
        }

        /// <inheritdoc/>
        public override void Extract(string[] startPaths, string destination)
        {
            if (Salt is not null)
            {
                Reader.Salt = Salt.Value;
            }

            foreach (var startPath in startPaths)
            {
                Reader.Traverse(startPath,
                    (dir) => Console.WriteLine($"Extracting {ReplaceControlChars(dir)} ..."),
                    (file) => ExtractFile(file, destination),
                    (nonexistent) =>
                    {
                        if (nonexistent == "/")
                        {
                            Console.Error.WriteLine("Top level directory is missing; " +
                                "try a partial extraction or use --raw to dump entries");
                        }
                        else if (PrintNotFoundMessage)
                        {
                            Console.Error.WriteLine($"Path {ReplaceControlChars(nonexistent)} " +
                                $"was not found");
                            notFound++;
                        }
                    });
            }
        }

        private void ExtractFile(string file, string destination)
        {
            // The directory listing of core.scs only lists itself, but as a file, breaking everything
            if (file == "/") return;

            var fileWithoutSlash = RemoveInitialSlash(file);
            var sanitized = SanitizePath(fileWithoutSlash);
            var outputPath = Path.Combine(destination, sanitized);
            if (fileWithoutSlash != sanitized)
            {
                PrintRenameWarning(file, sanitized);
                renamed++;
            }
            ExtractToFile(file, outputPath, () => ExtractToFileInner(file, outputPath));
        }

        protected void ExtractToFile(string displayedPath, string outputPath,
            Action extractToFileCall)
        {
            if (!Overwrite && File.Exists(outputPath))
            {
                skipped++;
                return;
            }

            displayedPath = RemoveInitialSlash(displayedPath);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                extractToFileCall();
                extracted++;
            }
            catch (InvalidDataException idex)
            {
                Console.Error.WriteLine($"Unable to extract {ReplaceControlChars(displayedPath)}:");
                Console.Error.WriteLine(idex.Message);
                failed++;
            }
            catch (AggregateException agex)
            {
                Console.Error.WriteLine($"Unable to extract {ReplaceControlChars(displayedPath)}:");
                Console.Error.WriteLine(agex.ToString());
                failed++;
            }
            catch (IOException ioex)
            {
                Console.Error.WriteLine($"Unable to extract {ReplaceControlChars(displayedPath)}:");
                Console.Error.WriteLine(ioex.Message);
                failed++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unable to extract {ReplaceControlChars(displayedPath)}:");
                Console.Error.WriteLine(ex.ToString());
                failed++;
            }
        }

        private void ExtractToFileInner(string file, string outputPath)
        {
            if (Path.GetExtension(file) == ".sii")
            {
                var sii = Reader.Extract(file)[0];
                sii = SiiFile.Decode(sii);
                File.WriteAllBytes(outputPath, sii);
            }
            else
            {
                Reader.ExtractToFile(file, outputPath);
            }
        }

        private void PrintExtractingMessage(string scsName, string archivePath)
        {
            var path = Combine(scsName, archivePath);
            Console.Out.WriteLine($"Extracting {ReplaceControlChars(path)} ...");
        }

        public override void PrintContentSummary()
        {
            var dirCount = Reader.Entries.Count(x => x.Value.IsDirectory);
            Console.WriteLine($"Opened {Path.GetFileName(scsPath)}: " +
                $"HashFS v{Reader.Version} archive; {Reader.Entries.Count} entries " +
                $"({Reader.Entries.Count - dirCount} files, {dirCount} directory listings)");
        }

        public override void PrintExtractionResult()
        {
            Console.WriteLine($"{extracted} extracted ({renamed} renamed), {skipped} skipped, " +
                $"{notFound} not found, {failed} failed");
            PrintRenameSummary(renamed);
        }

        public override void PrintPaths(string[] startPaths)
        {
            foreach (var startPath in startPaths)
            {
                Reader.Traverse(startPath,
                    (dir) => Console.WriteLine(ReplaceControlChars(dir)),
                    (file) => Console.WriteLine(ReplaceControlChars(file)),
                    (nonexistent) =>
                    {
                        if (nonexistent == "/")
                        {
                            Console.Error.WriteLine("Top level directory is missing; " +
                                "try a partial extraction or use --raw to dump entries");
                        }
                        else if (PrintNotFoundMessage)
                        {
                            Console.Error.WriteLine($"Path {ReplaceControlChars(nonexistent)} " +
                                $"was not found");
                        }
                    });
            }
        }

        public override void Dispose()
        {
            Reader.Dispose();
        }
    }
}
