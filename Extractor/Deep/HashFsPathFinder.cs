using Extractor.Properties;
using Sprache;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TruckLib.HashFs;
using static Extractor.PathUtils;

namespace Extractor.Deep
{
    /// <summary>
    /// Searches the entries of a HashFS archive for paths contained in it.
    /// </summary>
    internal class HashFsPathFinder
    {
        /// <summary>
        /// File paths that were discovered by <see cref="Find"/>.
        /// </summary>
        public HashSet<string> FoundFiles { get; private set; }

        /// <summary>
        /// Decoy file paths that were discovered by <see cref="Find"/>.
        /// Since decoy files typically contain junk data, they are kept separate
        /// from all other discovered paths.
        /// </summary>
        public HashSet<string> FoundDecoyFiles { get; private set; }

        /// <summary>
        /// File paths that are referenced by files in this archive.
        /// </summary>
        public HashSet<string> ReferencedFiles { get; private set; }

        /// <summary>
        /// This is used to find the absolute path of tobj files that are referenced in 
        /// mat files for which the mat path was not found and the tobj is referenced as
        /// relative path only.
        /// </summary>
        private static readonly string[] RootsToSearchForRelativeTobj = [
            "/material", "/model", "/model2", "/prefab", "/prefab2", "/vehicle"
        ];
        
        /// <summary>
        /// The HashFS reader.
        /// </summary>
        private readonly IHashFsReader reader;

        /// <summary>
        /// Additional start paths which the user specified with the <c>--additional</c> parameter.
        /// </summary>
        private readonly IList<string> additionalStartPaths;

        /// <summary>
        /// Paths that have been visited so far.
        /// </summary>
        private readonly HashSet<string> visited;

        /// <summary>
        /// Entries that have been visited so far.
        /// </summary>
        private readonly HashSet<IEntry> visitedEntries;

        /// <summary>
        /// Junk entries identified by DeleteJunkEntries.
        /// </summary>
        private readonly Dictionary<ulong, IEntry> junkEntries;

        /// <summary>
        /// The <see cref="FilePathFinder"/> instance.
        /// </summary>
        private readonly FilePathFinder fpf;

        /// <summary>
        /// Directories discovered during the first phase which might contain tobj files.
        /// This is used to find the absolute path of tobj files that are referenced in 
        /// mat files for which the mat path was not found and the tobj is referenced as
        /// relative path only.
        /// </summary>
        private readonly HashSet<string> dirsToSearchForRelativeTobj;

        // Synchronization primitives for thread-safety
        private readonly object visitedLock = new();
        private readonly object visitedEntriesLock = new();
        private readonly object referencedFilesLock = new();
        private readonly object dirsLock = new();
        private readonly object foundFilesLock = new();
        private readonly object junkEntriesLock = new();
        private readonly object readerLock = new();

        // Reader pool for parallel, safe extraction
        private readonly Func<IHashFsReader> readerFactory;
        private readonly int readerPoolSize;
        private System.Collections.Concurrent.ConcurrentBag<IHashFsReader> readerPool;
        private System.Collections.Concurrent.ConcurrentDictionary<IHashFsReader, byte> readerPoolMembers;
        private System.Threading.SemaphoreSlim readerSemaphore;

        // Timing/metrics (ticks accumulated across threads)
        private long extractTicks = 0;
        private long parseTicks = 0;
        private int filesParsed = 0;
        private long bytesInflated = 0;
        private long decompWallStartTs = 0;
        private long decompWallEndTs = 0;
        private readonly object decompWallLock = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> uniqueParsed = new();

        public TimeSpan ExtractTime => TimeSpan.FromTicks(System.Threading.Interlocked.Read(ref extractTicks));
        public TimeSpan ParseTime => TimeSpan.FromTicks(System.Threading.Interlocked.Read(ref parseTicks));
        public int FilesParsed => System.Threading.Interlocked.CompareExchange(ref filesParsed, 0, 0);
        public long BytesInflated => System.Threading.Interlocked.Read(ref bytesInflated);
        public TimeSpan ExtractWallTime
        {
            get
            {
                var start = System.Threading.Interlocked.Read(ref decompWallStartTs);
                var end = System.Threading.Interlocked.Read(ref decompWallEndTs);
                if (start == 0 || end <= start) return TimeSpan.Zero;
                double ticks = (end - start);
                double seconds = ticks / (double)Stopwatch.Frequency;
                return TimeSpan.FromSeconds(seconds);
            }
        }
        public int UniqueFilesParsed => uniqueParsed.Count;

        private readonly bool singleThreaded;

        public HashFsPathFinder(
            IHashFsReader reader,
            IList<string> additionalStartPaths = null,
            Dictionary<ulong, IEntry> junkEntries = null,
            bool singleThreaded = false,
            Func<IHashFsReader> readerFactory = null,
            int readerPoolSize = 1)
        {
            this.reader = reader;
            this.additionalStartPaths = additionalStartPaths;
            this.junkEntries = junkEntries ?? [];
            this.singleThreaded = singleThreaded;
            this.readerFactory = readerFactory;
            this.readerPoolSize = Math.Max(1, readerPoolSize);

            visited = [];
            visitedEntries = [];
            FoundFiles = [];
            dirsToSearchForRelativeTobj = [];
            ReferencedFiles = [];

            fpf = new FilePathFinder(ReferencedFiles, dirsToSearchForRelativeTobj, reader, referencedFilesLock, dirsLock);
        }

        /// <summary>
        /// Scans the archive for paths and writes the results to <see cref="FoundFiles"/> 
        /// and <see cref="FoundDecoyFiles"/>.
        /// </summary>
        public void Find()
        {
            InitializeReaderPool();
            var potentialPaths = LoadStartPaths();
            if (additionalStartPaths is not null)
                potentialPaths.UnionWith(additionalStartPaths);
            ExplorePotentialPaths(potentialPaths);

            var morePaths = FindPathsInUnvisited();
            ExplorePotentialPaths(morePaths);

            FoundDecoyFiles = FindDecoyPaths();

            DisposeReaderPool();
        }

        private void InitializeReaderPool()
        {
            if (singleThreaded)
                return;
            if (readerFactory is null || readerPoolSize <= 1)
                return;
            readerSemaphore = new System.Threading.SemaphoreSlim(readerPoolSize, readerPoolSize);
            readerPool = new System.Collections.Concurrent.ConcurrentBag<IHashFsReader>();
            readerPoolMembers = new System.Collections.Concurrent.ConcurrentDictionary<IHashFsReader, byte>();
            for (int i = 0; i < readerPoolSize; i++)
            {
                try
                {
                    var r = readerFactory();
                    readerPool.Add(r);
                    readerPoolMembers.TryAdd(r, 0);
                }
                catch
                {
                    // Fall back to fewer readers if creation fails
                    break;
                }
            }
        }

        private IHashFsReader AcquireReader()
        {
            if (readerPool is null)
                return null;
            readerSemaphore.Wait();
            if (readerPool.TryTake(out var r))
                return r;
            // Shouldn't happen, but create a temporary reader if factory available
            return readerFactory?.Invoke();
        }

        private void ReleaseReader(IHashFsReader r)
        {
            if (readerSemaphore is null)
                return;

            try
            {
                if (r is not null && readerPool is not null && readerPoolMembers is not null && readerPoolMembers.ContainsKey(r))
                {
                    readerPool.Add(r);
                }
                else
                {
                    try { r?.Dispose(); } catch { }
                }
            }
            finally
            {
                try { readerSemaphore.Release(); } catch { }
            }
        }

        private void DisposeReaderPool()
        {
            if (readerPool is null)
                return;
            while (readerPool.TryTake(out var r))
            {
                try { r.Dispose(); } catch { }
                if (readerPoolMembers is not null)
                {
                    readerPoolMembers.TryRemove(r, out _);
                }
            }
            readerSemaphore?.Dispose();
            readerSemaphore = null;
            readerPool = null;
            readerPoolMembers = null;
        }

        /// <summary>
        /// Loads the set of initial paths which the path finder will check for.
        /// </summary>
        /// <returns>The initial paths.</returns>
        private static PotentialPaths LoadStartPaths()
        {
            using var inMs = new MemoryStream(Resources.DeepStartPaths);
            using var ds = new DeflateStream(inMs, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            ds.CopyTo(outMs);
            var lines = Encoding.UTF8.GetString(outMs.ToArray());
            return LinesToHashSet(lines);
        }

        private void ExplorePotentialPaths(PotentialPaths potentialPaths)
        {
            do
            {
                potentialPaths = FindPaths(potentialPaths);
                potentialPaths.ExceptWith(visited);
            }
            while (potentialPaths.Count > 0);
        }

        /// <summary>
        /// Scans entries which were not visited in the first phase.
        /// </summary>
        /// <returns>Newly discovered paths.</returns>
        private PotentialPaths FindPathsInUnvisited()
        {
            if (singleThreaded)
            {
                // Sequential/legacy behavior with consistent metrics
                PotentialPaths potentialPaths = [];
                var unvisited = reader.Entries.Values.Except(visitedEntries);
                foreach (var entry in unvisited)
                {
                    visitedEntries.Add(entry);
                    if (junkEntries.ContainsKey(entry.Hash))
                        continue;
                    if (entry.IsDirectory)
                        continue;
                    if (entry is EntryV2 v2 && v2.TobjMetadata is not null)
                        continue;

                    byte[] fileBuffer;
                    try
                    {
                        var swExt = Stopwatch.StartNew();
                        lock (readerLock)
                        {
                            MarkDecompWallStart();
                            fileBuffer = reader.Extract(entry, "")[0];
                            MarkDecompWallEnd();
                        }
                        swExt.Stop();
                        System.Threading.Interlocked.Add(ref extractTicks, swExt.ElapsedTicks);
                        System.Threading.Interlocked.Add(ref bytesInflated, fileBuffer.LongLength);
                    }
                    catch (InvalidDataException)
                    {
                        #if DEBUG
                            Console.WriteLine($"Unable to decompress, likely junk: {entry.Hash:X16}");
                        #endif
                        junkEntries.TryAdd(entry.Hash, entry);
                        continue;
                    }
                    catch (Exception)
                    {
                        #if DEBUG
                            Debugger.Break();
                        #endif
                        continue;
                    }

                    var type = FileTypeHelper.Infer(fileBuffer);
                    if (type == FileType.Material)
                    {
                        // The file path of automats is derived from the CityHash64 of its content.
                        var contentHash = CityHash.CityHash64(fileBuffer, (ulong)fileBuffer.Length);
                        var contentHashStr = contentHash.ToString("x16");
                        var automatPath = $"/automat/{contentHashStr[..2]}/{contentHashStr}.mat";
                        potentialPaths.Add(automatPath);
                    }
                    try
                    {
                        var swParse = Stopwatch.StartNew();
                        var paths = fpf.FindPathsInFile(fileBuffer, null, type);
                        swParse.Stop();
                        System.Threading.Interlocked.Add(ref parseTicks, swParse.ElapsedTicks);
                        System.Threading.Interlocked.Increment(ref filesParsed);
                        uniqueParsed.TryAdd(entry.Hash.ToString("x16"), 0);

                        // Expand variants to keep behavior consistent with parallel branch
                        PotentialPaths expanded = [];
                        foreach (var path in paths)
                        {
                            expanded.AddWithVariants(path);
                        }
                        foreach (var p in expanded)
                        {
                            potentialPaths.Add(p, visited);
                        }
                    }
                    #if DEBUG
                    catch (Exception ex)
                    #else
                    catch (Exception)
                    #endif
                    {
                        #if DEBUG
                            var extension = FileTypeHelper.FileTypeToExtension(type);
                            Console.Error.WriteLine($"Unable to parse {entry.Hash:X16}{extension}: " +
                                $"{ex.GetType().Name}: {ex.Message}");
                        #endif
                        continue;
                    }
                }
                return potentialPaths;
            }
            else
            {
                var bag = new ConcurrentBag<string>();
                var unvisited = reader.Entries.Values.Except(visitedEntries).ToList();
                Parallel.ForEach(unvisited, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, entry =>
                {
                    lock (visitedEntriesLock)
                    {
                        visitedEntries.Add(entry);
                    }
                    lock (junkEntriesLock)
                    {
                        if (junkEntries.ContainsKey(entry.Hash))
                        {
                            return;
                        }
                    }
                    if (entry.IsDirectory)
                    {
                        return;
                    }
                    if (entry is EntryV2 v2 && v2.TobjMetadata is not null)
                    {
                        return;
                    }

                    byte[] fileBuffer;
                    try
                    {
                        var swExt = Stopwatch.StartNew();
                        if (readerPool is null)
                        {
                            lock (readerLock)
                            {
                                fileBuffer = reader.Extract(entry, "")[0];
                            }
                        }
                        else
                        {
                            IHashFsReader r = null;
                            try
                            {
                                r = AcquireReader();
                                MarkDecompWallStart();
                                fileBuffer = r.Extract(entry, "")[0];
                                MarkDecompWallEnd();
                            }
                            finally
                            {
                                ReleaseReader(r);
                            }
                        }
                        swExt.Stop();
                        System.Threading.Interlocked.Add(ref extractTicks, swExt.ElapsedTicks);
                        System.Threading.Interlocked.Add(ref bytesInflated, fileBuffer.LongLength);
                    }
                    catch (InvalidDataException)
                    {
                        #if DEBUG
                            Console.WriteLine($"Unable to decompress, likely junk: {entry.Hash:X16}");
                        #endif
                        lock (junkEntriesLock)
                        {
                            if (!junkEntries.ContainsKey(entry.Hash))
                                junkEntries[entry.Hash] = entry;
                        }
                        return;
                    }
                    catch (Exception)
                    {
                        #if DEBUG
                            Debugger.Break();
                        #endif
                        return;
                    }

                    var type = FileTypeHelper.Infer(fileBuffer);
                    if (type == FileType.Material)
                    {
                        // The file path of automats is derived from the CityHash64 of its content,
                        // so we add this automat path to the set of paths to check
                        var contentHash = CityHash.CityHash64(fileBuffer, (ulong)fileBuffer.Length);
                        var contentHashStr = contentHash.ToString("x16");
                        var automatPath = $"/automat/{contentHashStr[..2]}/{contentHashStr}.mat";
                        bag.Add(automatPath);
                    }
                try
                {
                    var swParse = Stopwatch.StartNew();
                    var paths = fpf.FindPathsInFile(fileBuffer, null, type);
                    swParse.Stop();
                    System.Threading.Interlocked.Add(ref parseTicks, swParse.ElapsedTicks);
                    System.Threading.Interlocked.Increment(ref filesParsed);
                    uniqueParsed.TryAdd(entry.Hash.ToString("x16"), 0);

                    // Ensure variant expansion is applied (e.g., .mat <-> .tobj/.dds)
                    PotentialPaths expanded = [];
                    foreach (var path in paths)
                    {
                        expanded.AddWithVariants(path);
                    }
                    foreach (var p in expanded)
                    {
                        bag.Add(p);
                    }
                }
                    #if DEBUG
                    catch (Exception ex)
                    #else
                    catch (Exception)
                    #endif
                    {
                        #if DEBUG
                            var extension = FileTypeHelper.FileTypeToExtension(type);
                            Console.Error.WriteLine($"Unable to parse {entry.Hash:X16}{extension}: " +
                                $"{ex.GetType().Name}: {ex.Message}");
                        #endif
                        return;
                    }
                });
                return new PotentialPaths(bag);
            }
        }

        /// <summary>
        /// Finds decoy variants of paths that have been discovered.
        /// </summary>
        /// <returns>The decoy paths.</returns>
        private HashSet<string> FindDecoyPaths()
        {
            HashSet<string> decoyPaths = [];

            foreach (var path in FoundFiles)
            {
                var cleaned = RemoveNonAsciiOrInvalidChars(path);
                if (cleaned != path)
                {
                    if (junkEntries.ContainsKey(reader.HashPath(cleaned)))
                    {
                        decoyPaths.Add(cleaned);
                    }
                    else if (reader.FileExists(cleaned))
                    {
                        var entry = reader.GetEntry(cleaned);
                        visited.Add(cleaned);
                        visitedEntries.Add(entry);
                        decoyPaths.Add(cleaned);
                    }
                }
            }

            return decoyPaths;
        }

        /// <summary>
        /// Searches for potential paths in the given files and directories.
        /// All visited paths are added to <c>this.visited</c>, all visited paths that exist
        /// in the archive are added to <c>this.foundFiles</c>, and all new potential paths
        /// that have been discovered are returned.
        /// </summary>
        /// <param name="inputPaths">The files and directories to search.</param>
        /// <returns>The discovered potential paths.</returns>
        private PotentialPaths FindPaths(PotentialPaths inputPaths)
        {
            // Collect files to process first, then process them in parallel using the reader pool.
            HashSet<string> filesToProcess = [];

            foreach (var path in inputPaths)
            {
                lock (visitedLock)
                {
                    if (visited.Contains(path))
                        continue;
                }

                reader.Traverse(path,
                    (dir) =>
                    {
                        bool isNew;
                        lock (visitedLock)
                        {
                            isNew = visited.Add(dir);
                            if (isNew)
                            {
                                lock (visitedEntriesLock)
                                {
                                    visitedEntries.Add(reader.GetEntry(dir));
                                }
                            }
                        }
                        return isNew;
                    },
                    (file) =>
                    {
                        // Avoid scheduling files that are already visited
                        lock (visitedLock)
                        {
                            if (visited.Contains(file))
                                return;
                        }
                        filesToProcess.Add(file);
                    },
                    (_) => { }
                );
            }

            var bag = new ConcurrentBag<string>();
            var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, readerPoolSize) };
            Parallel.ForEach(filesToProcess, options, file =>
            {
                try
                {
                    var paths = FindPathsInFile(file);
                    foreach (var p in paths)
                    {
                        bag.Add(p);
                    }
                    lock (referencedFilesLock)
                    {
                        ReferencedFiles.UnionWith(paths);
                    }
                }
                #if DEBUG
                catch (Exception ex)
                #else
                catch (Exception)
                #endif
                {
                    #if DEBUG
                    Console.Error.WriteLine($"Unable to parse {ReplaceControlChars(file)}: " +
                        $"{ex.GetType().Name}: {ex.Message.Trim()}");
                    #endif
                }
            });

            return new PotentialPaths(bag);
        }

        /// <summary>
        /// Returns paths referenced in the given file.
        /// </summary>
        /// <param name="filePath">The path of the file in the archive.</param>
        /// <returns>Paths referenced in the file. If the file does not exist, an empty
        /// set is returned.</returns>
        private HashSet<string> FindPathsInFile(string filePath)
        {
            lock (visitedLock)
            {
                visited.Add(filePath);
            }

            lock (junkEntriesLock)
            {
                if (junkEntries.ContainsKey(reader.HashPath(filePath)))
                {
                    return [];
                }
            }

            if (!reader.FileExists(filePath))
            {
                return [];
            }

            lock (foundFilesLock)
            {
                FoundFiles.Add(filePath);
            }
            var fileEntry = reader.GetEntry(filePath);
            lock (visitedEntriesLock)
            {
                visitedEntries.Add(fileEntry);
            }
            AddDirToDirsToSearchForRelativeTobj(filePath);

            // skip HashFS v2 tobj/dds entries because we don't need to scan those
            if (fileEntry is EntryV2 v2 && v2.TobjMetadata is not null)
            {
                return [];
            }

            var fileType = FileTypeHelper.PathToFileType(filePath);
            if (FilePathFinder.IgnorableFileTypes.Contains(fileType))
            {
                return [];
            }

            var swExt = Stopwatch.StartNew();
            byte[] fileBuffer;
            if (readerPool is null)
            {
                lock (readerLock)
                {
                    MarkDecompWallStart();
                    fileBuffer = reader.Extract(fileEntry, filePath)[0];
                    MarkDecompWallEnd();
                }
            }
            else
            {
                IHashFsReader r = null;
                try
                {
                    r = AcquireReader();
                    MarkDecompWallStart();
                    fileBuffer = r.Extract(fileEntry, filePath)[0];
                    MarkDecompWallEnd();
                }
                finally
                {
                    ReleaseReader(r);
                }
            }
            swExt.Stop();
            System.Threading.Interlocked.Add(ref extractTicks, swExt.ElapsedTicks);
            System.Threading.Interlocked.Add(ref bytesInflated, fileBuffer.LongLength);
            if (fileType == FileType.Unknown)
            {
                fileType = FileTypeHelper.Infer(fileBuffer);
            }
            var swParse = Stopwatch.StartNew();
            var result = fpf.FindPathsInFile(fileBuffer, filePath, fileType);
            swParse.Stop();
            System.Threading.Interlocked.Add(ref parseTicks, swParse.ElapsedTicks);
            System.Threading.Interlocked.Increment(ref filesParsed);
            uniqueParsed.TryAdd(filePath ?? string.Empty, 0);
            return result;
        }

        private void MarkDecompWallStart()
        {
            var now = Stopwatch.GetTimestamp();
            lock (decompWallLock)
            {
                if (decompWallStartTs == 0 || now < decompWallStartTs)
                {
                    decompWallStartTs = now;
                }
            }
        }

        private void MarkDecompWallEnd()
        {
            var now = Stopwatch.GetTimestamp();
            lock (decompWallLock)
            {
                if (now > decompWallEndTs)
                {
                    decompWallEndTs = now;
                }
            }
        }

        private void AddDirToDirsToSearchForRelativeTobj(string filePath)
        {
            foreach (var root in RootsToSearchForRelativeTobj)
            {
                if (filePath.StartsWith(root))
                {
                    lock (dirsLock)
                    {
                        dirsToSearchForRelativeTobj.Add(GetParent(filePath));
                    }
                }
            }
        }
    }
}
