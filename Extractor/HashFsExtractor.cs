using Ionic.Zlib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;
using TruckLib.HashFs;
using static Extractor.Util;


namespace Extractor
{
    public class HashFsExtractor : Extractor
    {
        public IHashFsReader Reader { get; private set; }
        public ushort? Salt { get; set; } = null;
        public bool ForceEntryTableAtEnd { get; set; } = false;
        public bool PrintNotFoundMessage { get; set; } = true;

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

        public override void Extract(string[] startPaths, string destination)
        {
            if (Salt is not null)
            {
                Reader.Salt = Salt.Value;
            }

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
                        var startPathWithoutSlash = startPath.StartsWith('/') ? startPath[1..] : startPath;
                        var outputPath = Path.Combine(destination, SanitizePath(startPathWithoutSlash));
                        Console.Out.WriteLine($"Extracting {ReplaceControlChars(startPath)} ...");
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
                        }
                        break;
                }
            }
        }

        private void ExtractDirectory(string directory, string destination)
        {
            string scsName = Path.GetFileName(scsPath);
            Console.Out.WriteLine($"Extracting {scsName}{ReplaceControlChars(directory)} ...");

            var (subdirs, files) = Reader.GetDirectoryListing(directory);
            Directory.CreateDirectory(Path.Combine(destination, directory[1..]));
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
                        var e = Reader.GetEntry(subdir[..^1]);
                        e.IsDirectory = true;
                        ExtractDirectory(subdir[..^1], destination);
                        break;
                    case EntryType.NotFound:
                        Console.Error.WriteLine($"Directory {ReplaceControlChars(subdir)} is referenced" +
                            $" in a directory listing but could not be found in the archive");
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
                        continue;
                    case EntryType.Directory:
                        // usually safe to ignore because it just points to the directory itself again
                        continue;
                }

                // The directory listing of core.scs only lists itself, but as a file, breaking everything
                if (file == "/") continue;

                var outputPath = Path.Combine(destination, SanitizePath(file[1..]));
                ExtractToFile(file, outputPath, () => Reader.ExtractToFile(file, outputPath));
            }
        }

        private void ExtractToFile(string archivePath, string outputPath, Action extractToFileCall)
        {
            if (!Overwrite && File.Exists(outputPath))
            {
                return;
            }

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

        public void ExtractRaw(string destination)
        {
            var scsName = Path.GetFileName(scsPath);
            Console.Out.WriteLine($"Extracting {scsName} ...");
            var outputDir = Path.Combine(destination, scsName);
            Directory.CreateDirectory(outputDir);

            if (Salt is not null)
            {
                Reader.Salt = Salt.Value;
            }

            var seenOffsets = new HashSet<ulong>();

            foreach (var (key, entry) in Reader.Entries)
            {
                if (seenOffsets.Contains(entry.Offset)) continue;
                seenOffsets.Add(entry.Offset);

                // subdirectory listings are useless because the file names are relative
                if (entry.IsDirectory) continue;

                var outputPath = Path.Combine(outputDir, key.ToString("x"));
                ExtractToFile(key.ToString("x"), outputPath, () => Reader.ExtractToFile(entry, "", outputPath));
            }
        }

        public override void Dispose()
        {
            Reader.Dispose();
        }
    }
}
