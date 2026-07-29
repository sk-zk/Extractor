using Serilog;
using Sprache;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TruckLib;
using TruckLib.HashFs;
using TruckLib.Sii;
using static Extractor.ConsoleUtils;
using static Extractor.PathUtils;
using static Extractor.TextUtils;

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

        public override IFileSystem FileSystem => Reader;

        /// <summary>
        /// Whether a "not found" message should be printed if a start path
        /// does not exist in the archive.
        /// </summary>
        public bool PrintNotFoundMessage { get; set; } = true;

        /// <summary>
        /// Junk entries identified by DeleteJunkEntries.
        /// </summary>
        protected Dictionary<ulong, IEntry> junkEntries = [];

        /// <summary>
        /// Entries idenfitied by DeleteJunkEntries which are likely to be junk, but may not be.
        /// </summary>
        protected Dictionary<ulong, IEntry> maybeJunkEntries = [];

        private bool hasIdentifiedJunk;

        /// <summary>
        /// Paths which will need to be renamed.
        /// </summary>
        protected Dictionary<string, string> substitutions;

        /// <summary>
        /// The number of files which have been extracted successfully.
        /// </summary>
        protected int numExtracted;

        /// <summary>
        /// The number of files which have been skipped because the output path
        /// already exists and <see cref="Options.SkipIfExists"/> is set.
        /// </summary>
        protected int numSkipped;

        /// <summary>
        /// The number of files which failed to extract.
        /// </summary>
        protected int numFailed;

        /// <summary>
        /// The number of paths passed by the user which were not found in the archive.
        /// </summary>
        protected int numNotFound;

        /// <summary>
        /// The number of files which were skipped because they have been identified
        /// as junk data.
        /// </summary>
        protected int numJunk;

        private readonly ILogger logger;

        public HashFsExtractor(string scsPath, Options opt) : base(scsPath, opt)
        {
            logger = Log.ForContext<HashFsExtractor>();

            PrintNotFoundMessage = !opt.ExtractAllInDir && !opt.Quiet;

            Reader = HashFsReader.Open(scsPath, opt.ForceEntryTableAtEnd);
            PrintContentSummary();
            
            if (Reader is HashFsV2Reader v2 && v2.UnimplementedEntries.Count > 0)
            {
                Console.Error.WriteLine($"WARN: {v2.UnimplementedEntries.Count} entries with unknown chunk type " +
                    $"have been skipped; some files may be missing");
                logger.Warning("[{ScsName}] Skipped {Count} entries with unknown chunk type; some files may be missing",
                    ScsName, v2.UnimplementedEntries.Count);
            }

            bool hasSaltChanged;
            if (opt.Salt is not null)
            {
                Reader.Salt = opt.Salt.Value;
                hasSaltChanged = true;
            }
            else
            {
                hasSaltChanged = FixSaltIfNecessary();
            }

            if (hasSaltChanged)
            {
                // ^ Used as a hint that a certain DRM packer
                // may have packed this archive, requiring this method
                // to run.
                FixEntryOffsetsIfNecessary();
            }

            IdentifyJunkEntries();
        }

        /// <inheritdoc/>
        public override void Extract(string outputRoot)
        {
            if (opt.StartPaths.Count == 1 && opt.StartPaths[0] == "/")
            {
                AssertRootNotMissingOrEmpty();
            }

            var pathsToExtract = GetPathsToExtract(Reader, opt.StartPaths, opt.Filters,
                (nonexistent) =>
                {
                    if (PrintNotFoundMessage)
                    {
                        Console.Error.WriteLine($"Path {ReplaceControlChars(nonexistent)} " +
                            $"was not found");
                        numNotFound++;
                    }
                    logger.Error("[{ScsName}] Path \"{Path}\" was not found", ScsName, nonexistent);
                }
            );
            logger.Information("[{ScsName}] Found {Num} paths to extract", ScsName, pathsToExtract.Count);

            substitutions = DeterminePathSubstitutions(pathsToExtract);
            LogPathSubstitutions();

            ExtractFiles(pathsToExtract, outputRoot);

            WriteRenamedSummary(outputRoot);
            WriteModifiedSummary(outputRoot);
        }

        protected void LogPathSubstitutions()
        {
            logger.Information("[{ScsName}] {NumSubstitutions} paths will be renamed", ScsName, substitutions.Count);
            foreach (var (archivePath, sanitizedPath) in substitutions)
            {
                logger.Verbose("[{ScsName}] File \"{Path}\" will be renamed to \"{Sanitized}\"",
                    ScsName, archivePath, sanitizedPath);
            }
        }

        private void AssertRootNotMissingOrEmpty()
        {
            if (Reader.EntryExists("/") != EntryType.NotFound)
            {
                var listing = Reader.GetDirectoryListing("/");
                if (listing.Subdirectories.Count == 0 && listing.Files.Count == 0)
                {
                    logger.Error("[{ScsName}] Root directroy listing is empty; unable to proceed", ScsName);
                    throw new RootEmptyException();
                }
            }
            else
            {
                logger.Error("[{ScsName}] Root directroy listing is missing; unable to proceed", ScsName);
                throw new RootMissingException();
            }
        }

        protected void ExtractFiles(IList<string> pathsToExtract, string outputRoot, bool ignoreMissing = false)
        {
            foreach (var archivePath in pathsToExtract)
            {
                ExtractFile(archivePath, outputRoot, ignoreMissing);
            }
        }

        internal static Dictionary<string, string> DeterminePathSubstitutions(IList<string> pathsToExtract)
        {
            Dictionary<string, string> substitutions = [];
            foreach (var path in pathsToExtract)
            {
                var sanitized = SanitizePath(path);
                if (sanitized != path)
                {
                    substitutions.Add(path, sanitized);
                }
            }
            return substitutions;
        }

        internal static List<string> GetPathsToExtract(IHashFsReader reader, IList<string> startPaths, 
            IList<Regex> filters, Action<string> onVisitNonexistent)
        {
            List<string> pathsToExtract = [];
            foreach (var path in startPaths)
            {
                reader.Traverse(path, 
                    (_) => { }, 
                    (file) =>
                    {
                        if (file != "/" && MatchesFilters(filters, file))
                        {
                            pathsToExtract.Add(file);
                        }
                    },
                    onVisitNonexistent);
            }
            return pathsToExtract;
        }

        protected void ExtractFile(string archivePath, string outputRoot, bool ignoreMissing = false)
        {
            if (!Reader.FileExists(archivePath))
            {
                if (ignoreMissing)
                {
                    return;
                }
                else
                {
                    logger.Error("[{ScsName}] File \"{Path}\" was not found", ScsName, archivePath);
                    throw new FileNotFoundException();
                }
            }

            if (!substitutions.TryGetValue(archivePath, out string filePath))
            {
                filePath = archivePath;
            }
            var outputPath = Path.Combine(outputRoot, RemoveInitialSlash(filePath));
            if (archivePath != filePath)
            {
                renamedFiles.Add((archivePath, filePath));
            }

            if (!opt.Quiet)
            {
                PrintExtractionMessage(archivePath, ScsName);
            }

            if (Overwrite || !File.Exists(outputPath))
            {
                logger.Verbose("[{ScsName}] Extracting \"{Path}\"", ScsName, archivePath);
                ExtractToDisk(archivePath, outputPath);
            }
            else
            {
                numSkipped++;
            }
        }

        protected void ExtractToDisk(string archivePath, string outputPath)
        {
            Reader.TryGetEntry(archivePath, out var entry);
            ExtractToDisk(entry, archivePath, outputPath);       
        }

        protected void ExtractToDisk(IEntry entry, string archivePath, string outputPath)
        {
            try
            {
                var buffers = Reader.Extract(entry, archivePath);

                var extension = Path.GetExtension(archivePath).ToLowerInvariant();
                if (extension == ".sii")
                {
                    buffers[0] = SiiFile.Decode(buffers[0]);
                }

                var wasModified = false;
                if (!opt.DisablePathUpdates)
                {
                    wasModified = PerformSubstitutionIfRequired(archivePath, ref buffers[0], substitutions);
                    if (wasModified)
                    {
                        logger.Verbose("[{ScsName}] \"{Path}\" has been modified to update renamed paths",
                            ScsName, archivePath);
                    }
                }

                if (!opt.DryRun)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                    File.WriteAllBytes(outputPath, buffers[0]);
                }

                if (!opt.DryRun && entry is EntryV2 v2 && v2.TobjMetadata is not null)
                {
                    var ddsPath = Path.ChangeExtension(outputPath, "dds");
                    if (Overwrite || !File.Exists(ddsPath))
                    {
                        File.WriteAllBytes(ddsPath, buffers[1]);
                    }
                }

                numExtracted++;
                if (wasModified)
                {
                    // This is down here at the end so it only runs
                    // if no IO errors occurred
                    modifiedFiles.Add(archivePath);
                }
            }
            catch (InvalidDataException idex)
            {
                PrintExtractionFailure(archivePath, idex.Message);
                logger.Error(idex, "[{ScsName}] Unable to extract \"{Path}\" ({Hash:x16} at {Offset})", 
                    ScsName, archivePath, entry.Hash, entry.Offset);
            }
            catch (AggregateException agex)
            {
                PrintExtractionFailure(archivePath, agex.ToString());
                logger.Error(agex, "[{ScsName}] Unable to extract \"{Path}\" ({Hash:x16} at {Offset})",
                    ScsName, archivePath, entry.Hash, entry.Offset);
            }
            catch (IOException ioex)
            {
                PrintExtractionFailure(archivePath, ioex.Message);
                logger.Error(ioex, "[{ScsName}] Unable to extract \"{Path}\" ({Hash:x16} at {Offset})",
                    ScsName, archivePath, entry.Hash, entry.Offset);
            }
            catch (Exception ex)
            {
                PrintExtractionFailure(archivePath, ex.ToString());
                logger.Error(ex, "[{ScsName}] Unable to extract \"{Path}\" ({Hash:x16} at {Offset})",
                    ScsName, archivePath, entry.Hash, entry.Offset);
            }
        }

        private void IdentifyJunkEntries()
        {
            if (hasIdentifiedJunk)
                return;

            // Kick out entries with offsets greater than the size of the archive.
            logger.Information("[{ScsName}] Eliminating entries with impossibly large offsets", ScsName);
            foreach (var (_, entry) in Reader.Entries)
            {
                if (entry.Offset > (ulong)Reader.BaseReader.BaseStream.Length)
                {
                    MarkAsConfirmedJunk(entry);
                }
            }

            // Find all entries with duplicate offsets and group them by offset.
            logger.Information("[{ScsName}] Cleaning up entries with duplicate offsets", ScsName);
            var visitedOffsets = new Dictionary<ulong, IEntry>();
            var junk = new Dictionary<ulong, List<IEntry>>();
            foreach (var (_, entry) in Reader.Entries)
            {
                if (junkEntries.ContainsKey(entry.Hash))
                    continue;

                if (!visitedOffsets.TryAdd(entry.Offset, entry))
                {
                    if (!junk.TryGetValue(entry.Offset, out var list))
                    {
                        list = [visitedOffsets[entry.Offset]];
                        junk.Add(entry.Offset, list);
                    }
                    list.Add(entry);
                }
            }

            // At most one of these files may contain actual data, so let's try
            // to figure out which of these entries it could be.
            // Entries which are guaranteed to be junk are added to `junkEntries`;
            // entries which could potentially contain the actual file are added
            // to `maybeJunkEntries`.
            // Nothing in `junkEntries` is ever extracted. An entry in `maybeJunkEntries` 
            // is extracted if a path pointing to it exists.
            if (junk.Count > 0)
            {
                var allOffsets = Reader.Entries.Select(x => x.Value.Offset).Distinct().Order().ToList();
                foreach (var (offset, list) in junk)
                {
                    var idx = allOffsets.BinarySearch(offset);
                    var nextOffset = idx == allOffsets.Count - 1
                        ? (ulong)Reader.BaseReader.BaseStream.Length
                        : allOffsets[idx + 1];

                    Reader.BaseReader.BaseStream.Position = (long)offset;
                    var firstByte = Reader.BaseReader.ReadByte();
                    var isZlibCompressed = firstByte == 0x78; // probably good enough

                    long decompressedLength = -1;
                    if (isZlibCompressed)
                    {
                        try
                        {
                            Reader.BaseReader.BaseStream.Position = (long)offset;
                            var buffer = Reader.BaseReader.ReadBytes((int)(nextOffset - offset));
                            using var inMs = new MemoryStream(buffer);
                            using var zlibStream = new ZLibStream(inMs, CompressionMode.Decompress);
                            using var outMs = new MemoryStream();
                            zlibStream.CopyTo(outMs);
                            decompressedLength = outMs.Length;
                            var newPos = (ulong)Reader.BaseReader.BaseStream.Position;
                        }
                        catch 
                        {
                            isZlibCompressed = false;
                        }
                    }

                    foreach (var entry in list)
                    {
                        if (entry.IsCompressed != isZlibCompressed)
                        {
                            MarkAsConfirmedJunk(entry);
                        }
                        else if (nextOffset - entry.Offset < entry.CompressedSize)
                        {
                            MarkAsConfirmedJunk(entry);
                        }
                        else if (nextOffset - entry.Offset - entry.CompressedSize > 32)
                        {
                            MarkAsConfirmedJunk(entry);
                        }
                        else if (entry.IsCompressed)
                        {
                            if (entry.Size == decompressedLength)
                                MarkAsMaybeJunk(entry);
                            else
                                MarkAsConfirmedJunk(entry);
                        }
                        else
                        {
                            MarkAsMaybeJunk(entry);
                        }
                    }
                }
            }

            hasIdentifiedJunk = true;

            if (junkEntries.Count > 0 || maybeJunkEntries.Count > 0)
            {
                logger.Information("[{ScsName}] Found {NumJunkEntries} confirmed junk entries " +
                    "and {NumMaybeJunk} potential junk entries",
                    ScsName, junkEntries.Count, maybeJunkEntries.Count);
            }

            void MarkAsConfirmedJunk(IEntry entry)
            {
                logger.Verbose("[{ScsName}] Identified {Hash:x16} ({Offset}) as junk", 
                    ScsName, entry.Hash, entry.Offset);
                junkEntries.Add(entry.Hash, entry);
                numJunk++;
            }

            void MarkAsMaybeJunk(IEntry entry)
            {
                logger.Verbose("[{ScsName}] Identified {Hash:x16} ({Offset}) as potential junk", 
                    ScsName, entry.Hash, entry.Offset);
                maybeJunkEntries.Add(entry.Hash, entry);
                numJunk++;
            }
        }

        private bool FixSaltIfNecessary()
        {
            const string fileToTest = "manifest.sii";
   
            if (Reader.TryGetEntry(fileToTest, out var _) == EntryType.NotFound)
            {
                // No manifest => don't need to run this
                return false;
            }

            try
            {
                // Decompresses correctly => no change needed
                Reader.Extract(fileToTest);
                return false;
            }
            catch
            {
                // Otherwise, iterate all salts until we find the good one
                Console.Error.WriteLine("Salt may be incorrect; attempting to fix ...");
                logger.Warning("[{ScsName}] {TestedFile} exists but fails to decompress, " +
                    "possibly indicating MG salt shenanigans. " +
                    "Attempting to fix", ScsName, fileToTest);
                for (int i = 0; i < ushort.MaxValue; i++)
                {
                    Reader.Salt = (ushort)i;
                    if (Reader.EntryExists(fileToTest) != EntryType.NotFound)
                    {
                        try
                        {
                            Reader.Extract(fileToTest);
                            Console.Error.WriteLine($"Salt set to {i}");
                            logger.Warning("[{ScsName}] Found working salt: {Salt}", ScsName, i);
                            return true;
                        }
                        catch { /* Failed to decompress => continue */ }
                    }
                }
                Console.Error.WriteLine("Unable to find true salt");
                logger.Error("[{ScsName}] Unable to find true salt", ScsName);
            }
            return false;
        }

        private void FixEntryOffsetsIfNecessary()
        {
            // Archives packed by a certain DRM packer may contain entries
            // for which the stated offset is incorrect and the actual start of
            // the file is located ~100 bytes earlier or later.
            // I don't know how this "nudge" is derived from the metadata
            // (or if it can be derived from it at all - maybe these values are
            // fetched from a remote server), so to fix the offsets of affected
            // entries, I just scan the surrounding bytes for zlib headers to find the
            // true offset of the file.

            if (Reader.Version != 1)
                return;

            logger.Information("[{ScsName}] Checking if any entry offsets were nudged by MG", ScsName);

            // The aforementioned "nudges" appear to always be one of these values:
            sbyte[] offsets = [-91, -75, -59, -43, -27, 37, 53, 69, 85, 101];

            var bufferSize = -offsets[0] + offsets[^1] + 2;
            var buffer = new byte[bufferSize];
            List<long> zlibHeaders = [];

            foreach (var (key, entry) in Reader.Entries)
            {
                var entryV1 = (EntryV1)entry;
                if (!entryV1.Flags[7] || !entry.IsCompressed)
                {
                    // ^ Non-standard flag set by the packer
                    //   if it has tampered with the offset.
                    continue;
                }

                var startOffset = (long)entry.Offset + offsets[0];
                if (startOffset < 0 || startOffset > Reader.BaseReader.BaseStream.Length)
                {
                    continue;
                }

                Reader.BaseReader.BaseStream.Position = startOffset;
                Reader.BaseReader.BaseStream.ReadExactly(buffer, 0, bufferSize);
                zlibHeaders.Clear();

                foreach (var offset in offsets)
                {
                    var bufferIdx = offset - offsets[0];
                    if (buffer[bufferIdx] == 0x78 && buffer[bufferIdx + 1] == 0x9c)
                    {
                        zlibHeaders.Add(offset);
                    }
                }

                if (zlibHeaders.Count == 1)
                {
                    // There's only one zlib header in the target range,
                    // so this is probably the file we're looking for.
                    SetCorrectedOffset(entryV1, zlibHeaders[0]);
                }
                else if (zlibHeaders.Count > 1)
                {
                    // More than one zlib header in the target range - let's find out
                    // which of the candidate streams is our entry.
                    FindEntryInCandidates(zlibHeaders, entryV1);
                }
                else
                {
                    logger.Debug("[{ScsName}] No zlib header found in target range for {Hash:x16}", ScsName, entry);
                }
            }

            void SetCorrectedOffset(EntryV1 entry, long nudge)
            {
                var newOffset = (ulong)((long)entry.Offset + nudge);
                logger.Debug("[{ScsName}] Corrected offset of {Hash:x16} from {Old} to {New}",
                    ScsName, entry, entry.Offset, newOffset);
                entry.Offset = newOffset;
                Reader.Entries[entry.Hash] = entry;
            }

            void FindEntryInCandidates(List<long> zlibHeaders, EntryV1 entry)
            {
                for (int i = 0; i < zlibHeaders.Count - 1; i++)
                {
                    var maxPossibleSize = zlibHeaders[i + 1] - zlibHeaders[i];
                    if (entry.CompressedSize <= maxPossibleSize)
                    {
                        var matches = SetCorrectedOffsetIfSizeMatches(entry, zlibHeaders[i]);
                        if (matches) return;
                    }
                }
                SetCorrectedOffsetIfSizeMatches(entry, zlibHeaders[^1]);
            }

            bool SetCorrectedOffsetIfSizeMatches(EntryV1 entry, long nudge)
            {
                var realSize = GetRealDecompressedSize(
                    (long)entry.Offset + nudge,
                    entry.Size);
                if (realSize == entry.Size)
                {
                    SetCorrectedOffset(entry, nudge);
                    return true;
                }
                return false;
            }

            long GetRealDecompressedSize(long streamPos, long decompressedSize)
            {
                try
                {
                    Reader.BaseReader.BaseStream.Position = streamPos;
                    using var zlibStream = new ZLibStream(Reader.BaseReader.BaseStream,
                        CompressionMode.Decompress, true);
                    using var outputStream = new MemoryStream();
                    BinaryUtils.CopyStream(zlibStream, outputStream, decompressedSize);
                    return outputStream.Length;
                }
                catch (InvalidDataException)
                {
                    return -1;
                }
            }
        }

        public override void PrintContentSummary()
        {
            var dirCount = Reader.Entries.Count(x => x.Value.IsDirectory);
            Console.Error.WriteLine($"Opened {Path.GetFileName(ScsPath)}: " +
                $"HashFS v{Reader.Version} archive; {Reader.Entries.Count} entries " +
                $"({Reader.Entries.Count - dirCount} files, {dirCount} directory listings)");
            logger.Information("[{ScsName}] HashFS v{HashFsVersion}; " +
                "{EntryCount} entries ({FileCount} files, {DirCount} directory listings)",
                ScsName, Reader.Version, Reader.Entries.Count, Reader.Entries.Count - dirCount, dirCount);
        }

        public override void PrintExtractionResult()
        {
            Console.Error.WriteLine($"{numExtracted} extracted " +
                $"({renamedFiles.Count} renamed, {modifiedFiles.Count} modified), " +
                $"{numSkipped} skipped, {numNotFound} not found, {numJunk} junk, {numFailed} failed");
            logger.Information("[{ScsName}] {NumExtracted} extracted " +
                "({NumRenamed} renamed, {NumModified} modified), " +
                "{NumSkipped} skipped, {NumNotFound} not found, {NumJunk} junk, {NumFailed} failed",
                ScsName, numExtracted, renamedFiles.Count, modifiedFiles.Count, 
                numSkipped, numNotFound, numJunk, numFailed);
            PrintRenameSummary(renamedFiles.Count, modifiedFiles.Count);
        }

        private void PrintExtractionFailure(string archivePath, string errorMessage)
        {
            Console.Error.WriteLine($"Unable to extract {ReplaceControlChars(archivePath)}:");
            Console.Error.WriteLine(errorMessage);
            numFailed++;
        }

        public override void PrintPaths(bool includeAll)
        {
            foreach (var path in opt.StartPaths)
            {
                Reader.Traverse(path,
                    (dir) => 
                    {
                        if (MatchesFilters(opt.Filters, dir))
                        {
                            Console.WriteLine(ReplaceControlChars(dir));
                        }
                    },
                    (file) =>
                    {
                        if (MatchesFilters(opt.Filters, file))
                        {
                            Console.WriteLine(ReplaceControlChars(file));
                        }
                    },
                    (nonexistent) =>
                    {
                        if (nonexistent == "/")
                        {
                            PrintRootMissingError();
                        }
                        else if (PrintNotFoundMessage)
                        {
                            Console.Error.WriteLine($"Path {ReplaceControlChars(nonexistent)} " +
                                $"was not found");
                        }
                    });
            }
        }

        public override List<Tree.Directory> GetDirectoryTree()
        {
            var trees = opt.StartPaths
                .Select(GetDirectoryTree)
                .ToList();
            return trees;
        }

        private Tree.Directory GetDirectoryTree(string root)
        {
            if (Reader.EntryExists(root) == EntryType.NotFound)
            {
                return null;
            }

            var dir = new Tree.Directory();
            dir.Path = root;
            var entry = Reader.GetEntry(root);
            entry.IsDirectory = true;

            var content = Reader.GetDirectoryListing(root);
            foreach (var subdir in content.Subdirectories)
            {
                var type = Reader.EntryExists(subdir);
                if (type == EntryType.File)
                {
                    var subentry = Reader.GetEntry(root);
                    subentry.IsDirectory = true;
                }
                else if (type == EntryType.NotFound)
                {
                    continue;
                }
                dir.Subdirectories.Add(Path.GetFileName(subdir), GetDirectoryTree(subdir));
            }
            foreach (var file in content.Files)
            {
                if (!junkEntries.ContainsKey(Reader.HashPath(file)))
                {
                    dir.Files.Add(file);
                }
            }
            return dir;
        }

        public override void Dispose()
        {
            Reader.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    internal class RootMissingException : Exception { }

    internal class RootEmptyException : Exception { }
}
