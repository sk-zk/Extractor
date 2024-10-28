﻿using Ionic.Zlib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TruckLib.HashFs;
using static Extractor.Util;

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

        protected int extracted;
        protected int skipped;
        protected int failed;
        protected int notFound;
        protected int renamed;

        public HashFsExtractor(string scsPath, bool overwrite) : base(scsPath, overwrite)
        {
            try
            {
                Reader = HashFsReader.Open(scsPath, ForceEntryTableAtEnd);
            }
            catch (InvalidDataException)
            {
                throw;
            }
        }

        /// <inheritdoc/>
        public override void Extract(string[] startPaths, string destination)
        {
            if (Salt is not null)
            {
                Reader.Salt = Salt.Value;
            }

            extracted = 0;
            skipped = 0;
            failed = 0;
            notFound = 0;
            renamed = 0;

            foreach (var startPath in startPaths)
            {
                switch (Reader.EntryExists(startPath))
                {
                    case EntryType.Directory:
                        ExtractDirectory(startPath, destination);
                        break;
                    case EntryType.File:
                        // TODO make sure this is actually a file
                        // and not a directory falsely labeled as one
                        var startPathWithoutSlash = RemoveInitialSlash(startPath);
                        var sanitized = SanitizePath(startPathWithoutSlash);
                        var outputPath = Path.Combine(destination, sanitized);
                        Console.Out.WriteLine($"Extracting {ReplaceControlChars(startPath)} ...");
                        if (startPathWithoutSlash != sanitized)
                        {
                            PrintRenameWarning(startPath, sanitized);
                            renamed++;
                        }
                        ExtractToFile(startPathWithoutSlash, outputPath,
                            () => Reader.ExtractToFile(startPathWithoutSlash, outputPath));
                        break;
                    case EntryType.NotFound:
                        if (startPath == "/")
                        {
                            Console.Error.WriteLine("Top level directory is missing; " +
                                "try a partial extraction or use --raw to dump entries");
                        }
                        else if (PrintNotFoundMessage)
                        {
                            Console.Error.WriteLine($"File or directory listing " +
                                $"{ReplaceControlChars(startPath)} does not exist");
                            notFound++;
                        }
                        break;
                }
            }
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
            Console.WriteLine($"{extracted} extracted, {renamed} renamed, {skipped} skipped, " +
                $"{notFound} not found, {failed} failed");
        }

        private void ExtractDirectory(string directory, string destination)
        {
            string scsName = Path.GetFileName(scsPath);
            var directoryWithoutSlash = RemoveInitialSlash(directory);

            Console.Out.WriteLine($"Extracting {scsName}/{ReplaceControlChars(directoryWithoutSlash)} ...");

            var (subdirs, files) = Reader.GetDirectoryListing(directory);
            Directory.CreateDirectory(Path.Combine(destination, directoryWithoutSlash));
            ExtractFiles(files, destination);
            ExtractSubdirectories(subdirs, destination);
        }

        private void ExtractSubdirectories(List<string> subdirs, string destination)
        {
            foreach (var subdir in subdirs)
            {
                var type = Reader.EntryExists(subdir);
                switch (type)
                {
                    case EntryType.Directory:
                        ExtractDirectory(subdir, destination);
                        break;
                    case EntryType.File:
                        // the subdir list contains a path which has not been
                        // marked as a directory. it might be one regardless though,
                        // so let's just attempt to extract it anyway
                        var subdirWithoutSlash = RemoveInitialSlash(subdir);
                        var e = Reader.GetEntry(subdirWithoutSlash);
                        e.IsDirectory = true;
                        ExtractDirectory(subdirWithoutSlash, destination);
                        break;
                    case EntryType.NotFound:
                        Console.Error.WriteLine($"Directory {ReplaceControlChars(subdir)} is referenced" +
                            $" in a directory listing but could not be found in the archive");
                        notFound++;
                        continue;
                }
            }
        }

        private void ExtractFiles(List<string> files, string destination)
        {
            foreach (var file in files)
            {
                var type = Reader.EntryExists(file);
                switch (type)
                {
                    case EntryType.NotFound:
                        Console.Error.WriteLine($"File {ReplaceControlChars(file)} is referenced in" +
                            $" a directory listing but could not be found in the archive");
                        notFound++;
                        continue;
                    case EntryType.Directory:
                        // usually safe to ignore because it just points to the directory itself again
                        continue;
                }

                // The directory listing of core.scs only lists itself, but as a file, breaking everything
                if (file == "/") continue;

                var fileWithoutSlash = RemoveInitialSlash(file);
                var sanitized = SanitizePath(fileWithoutSlash);
                var outputPath = Path.Combine(destination, sanitized);
                if (fileWithoutSlash != sanitized)
                {
                    PrintRenameWarning(file, sanitized);
                    renamed++;
                }
                ExtractToFile(file, outputPath, () => Reader.ExtractToFile(file, outputPath));
            }
        }

        protected void ExtractToFile(string archivePath, string outputPath, Action extractToFileCall)
        {
            if (!Overwrite && File.Exists(outputPath))
            {
                skipped++;
                return;
            }

            archivePath = RemoveInitialSlash(archivePath);

            try
            {
                extractToFileCall();
                extracted++;
            }
            catch (ZlibException zlex)
            {
                Console.Error.WriteLine($"Unable to extract {ReplaceControlChars(archivePath)}:");
                Console.Error.WriteLine(zlex.Message);
                failed++;
            }
            catch (InvalidDataException idex)
            {
                Console.Error.WriteLine($"Unable to extract {ReplaceControlChars(archivePath)}:");
                Console.Error.WriteLine(idex.Message);
                failed++;
            }
            catch (AggregateException agex)
            {
                Console.Error.WriteLine($"Unable to extract {ReplaceControlChars(archivePath)}:");
                Console.Error.WriteLine(agex.ToString());
                failed++;
            }
            catch (IOException ioex)
            {
                Console.Error.WriteLine($"Unable to extract {ReplaceControlChars(archivePath)}:");
                Console.Error.WriteLine(ioex.Message);
                failed++;
            }
        }

        public override void Dispose()
        {
            Reader.Dispose();
        }
    }
}
