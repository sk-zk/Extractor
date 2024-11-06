using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Extractor.PathUtils;
using static Extractor.ConsoleUtils;
using TruckLib.Sii;

namespace Extractor
{
    /// <summary>
    /// A zip archive reader which can handle archives with deliberately corrupted local file headers.
    /// </summary>
    public class ZipExtractor : Extractor
    {
        /// <summary>
        /// The entries in this archive.
        /// </summary>
        public List<CentralDirectoryFileHeader> Entries { get; private set; }

        private BinaryReader reader;
        private long endOfCentralDirOffset = -1;
        private EndOfCentralDirectory eocdRecord;

        private int extracted;
        private int skipped;
        private int empty;
        private int failed;
        private int renamed;

        public ZipExtractor(string scsPath, bool overwrite) 
            : base(scsPath, overwrite)
        {
            reader = new BinaryReader(new FileStream(scsPath, FileMode.Open, FileAccess.Read));
            reader.BaseStream.Position = 0;

            endOfCentralDirOffset = FindBytesBackwards(EndOfCentralDirectory.Signature);
            if (endOfCentralDirOffset < 0)
            {
                throw new InvalidDataException("No End Of Central Directory record found - probably not a zip file.");
            }
            ReadEndOfCentralDirectoryRecord();
            ReadCentralDirectory();
        }

        /// <inheritdoc/>
        public override void Extract(string[] startPaths, string destination)
        {
            string scsName = Path.GetFileName(scsPath);
            Console.Out.WriteLine($"Extracting {scsName} ...");

            startPaths = startPaths.Select(x => 
                x.StartsWith('/') || x.StartsWith('\\') 
                    ? x[1..] 
                    : x
                ).ToArray();

            extracted = 0;
            skipped = 0;
            empty = 0;
            failed = 0;
            renamed = 0;

            foreach (var entry in Entries)
            {
                if (!startPaths.Any(entry.FileName.StartsWith))
                {
                    continue;
                }

                if (entry.UncompressedSize == 0)
                {
                    empty++;
                    continue;
                }

                try
                {
                    Extract(entry, destination);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Unable to extract {ReplaceControlChars(entry.FileName)}:");
                    Console.Error.WriteLine(ex.Message);
                    failed++;
                }
            }
        }

        /// <summary>
        /// Extracts the given file from the archive to the destination directory.
        /// </summary>
        /// <param name="entry">The file to extract.</param>
        /// <param name="destination">The directory to extract the file to.</param>
        public void Extract(CentralDirectoryFileHeader entry, string destination)
        {
            var sanitized = SanitizePath(entry.FileName);

            // prevent traversing upwards with ".."
            sanitized = string.Join('/', sanitized.Split(['/', '\\']).Select(x => x == ".." ? "__" : x));

            var outputPath = Path.Combine(destination, sanitized);
            if (File.Exists(outputPath) && !Overwrite)
            {
                skipped++;
                return;
            }

            if (sanitized != entry.FileName)
            {
                PrintRenameWarning(entry.FileName, sanitized);
                renamed++;
            }

            var outputDir = Path.GetDirectoryName(outputPath);
            if (outputDir != null)
            {
                Directory.CreateDirectory(outputDir);
            }

            if (Path.GetExtension(entry.FileName) == ".sii")
            {
                using var ms = new MemoryStream();
                Extract(entry, ms);
                var sii = SiiFile.Decode(ms.ToArray());
                File.WriteAllBytes(outputPath, sii);
            }
            else
            {
                using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                Extract(entry, fs);
            }
        }

        /// <summary>
        /// Extracts the given file from the archive to a stream.
        /// </summary>
        /// <param name="entry">The file to extract.</param>
        /// <param name="outputStream">The stream to which the extracted file will be written.</param>
        /// <exception cref="NotSupportedException">Thrown if the compression method used 
        /// is not supported.</exception>
        public void Extract(CentralDirectoryFileHeader entry, Stream outputStream)
        {
            // Ignore the entire local file header, just like ETS2/ATS
            reader.BaseStream.Position = entry.LocalFileHeaderOffset + 26;
            var fileNameLength = reader.ReadUInt16();
            var extraFieldLength = reader.ReadUInt16();
            reader.BaseStream.Position += fileNameLength + extraFieldLength;

            switch (entry.CompressionMethod)
            {
                case CompressionMethod.Deflate:
                    var ds = new DeflateStream(reader.BaseStream, CompressionMode.Decompress);
                    ds.CopyTo(outputStream);
                    break;

                case CompressionMethod.None:
                    const int bufferSize = 4096;
                    var buffer = new byte[bufferSize];
                    for (int i = 0; i < entry.CompressedSize; i += bufferSize)
                    {
                        int blockSize = Math.Min(bufferSize, (int)entry.CompressedSize - i);
                        reader.BaseStream.Read(buffer, 0, blockSize);
                        outputStream.Write(buffer, 0, blockSize);
                    }
                    break;

                default:
                    throw new NotSupportedException(
                        $"Unsupported compression method {(int)entry.CompressionMethod}.");
            }
            extracted++;
        }

        public override void PrintContentSummary()
        {
            Console.WriteLine($"Opened {Path.GetFileName(scsPath)}: " +
                $"ZIP archive; {Entries.Count} entries");
        }

        public override void PrintExtractionResult()
        {
            Console.WriteLine($"{extracted} extracted, {renamed} renamed, {skipped} skipped, " +
                $"{empty} empty, {failed} failed");
            PrintRenameSummary(renamed);
        }

        /// <summary>
        /// Reads the End Of Central Directory record from the file.
        /// </summary>
        private void ReadEndOfCentralDirectoryRecord()
        {
            reader.BaseStream.Position = endOfCentralDirOffset + EndOfCentralDirectory.Signature.Length;
            eocdRecord = new EndOfCentralDirectory
            {
                DiskNr = reader.ReadUInt16(),
                CentralDirectoryDiskNr = reader.ReadUInt16(),
                DiskEntries = reader.ReadUInt16(),
                TotalEntries = reader.ReadUInt16(),
                CentralDirectorySize = reader.ReadUInt32(),
                CentralDirectoryOffset = reader.ReadUInt32(),
                CommentLength = reader.ReadUInt16()
            };
            eocdRecord.Comment = reader.ReadBytes(eocdRecord.CommentLength);
        }

        /// <summary>
        /// Reads the Central Directory file headers from a stream.
        /// </summary>
        private void ReadCentralDirectory()
        {
            reader.BaseStream.Position = eocdRecord.CentralDirectoryOffset;
            Entries = [];

            while (reader.BaseStream.Position <
                eocdRecord.CentralDirectoryOffset
                + eocdRecord.CentralDirectorySize)
            {
                var signature = reader.ReadBytes(4);
                if (!signature.SequenceEqual(CentralDirectoryFileHeader.Signature))
                {
                    return;
                }

                var file = new CentralDirectoryFileHeader
                {
                    VersionMadeBy = reader.ReadUInt16(),
                    VersionNeeded = reader.ReadUInt16(),
                    Flags = reader.ReadUInt16(),
                    CompressionMethod = (CompressionMethod)reader.ReadUInt16(),
                    FileModificationTime = reader.ReadUInt16(),
                    FileModificationDate = reader.ReadUInt16(),
                    Crc32 = reader.ReadUInt32(),
                    CompressedSize = reader.ReadUInt32(),
                    UncompressedSize = reader.ReadUInt32(),
                    FileNameLength = reader.ReadUInt16(),
                    ExtraFieldLength = reader.ReadUInt16(),
                    FileCommentLength = reader.ReadUInt16(),
                    DiskNr = reader.ReadUInt16(),
                    InternalAttribs = reader.ReadUInt16(),
                    ExternalAttribs = reader.ReadUInt32(),
                    LocalFileHeaderOffset = reader.ReadUInt32()
                };
                file.FileName = Encoding.UTF8.GetString(reader.ReadBytes(file.FileNameLength));
                file.ExtraField = reader.ReadBytes(file.ExtraFieldLength);
                file.FileComment = reader.ReadBytes(file.FileCommentLength);
                Entries.Add(file);
            }
        }

        /// <summary>
        /// Searches for a byte sequence in a stream, starting at the end of the file.
        /// </summary>
        /// <param name="bytes">The byte sequence to search for.</param>
        /// <returns>The absolute offset of the last occurrence of the sequence in the stream,
        /// or -1 if the sequence is not found.</returns>
        private long FindBytesBackwards(byte[] bytes)
        {
            reader.BaseStream.Seek(-bytes.Length, SeekOrigin.End);
            int seqIdx = bytes.Length - 1;
            long offset = -1;
            while (reader.BaseStream.Position > 0)
            {
                var current = reader.ReadByte();
                if (bytes[seqIdx] == current)
                {
                    seqIdx--;
                    if (seqIdx < 0)
                    {
                        offset = reader.BaseStream.Seek(-1, SeekOrigin.Current);
                        break;
                    }
                }
                else
                {
                    seqIdx = bytes.Length - 1;
                }
                reader.BaseStream.Seek(-2, SeekOrigin.Current);
            }
            return offset;
        }

        public override void PrintPaths(string[] startPaths)
        {
            foreach (var entry in Entries)
            {
                Console.WriteLine(ReplaceControlChars(entry.FileName));
            }
        }

        public override void Dispose()
        {
            reader.Dispose();
        }

        public override Tree.Directory GetDirectoryTree(string root)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Represents an End Of Central Directory record of a zip file.
    /// </summary>
    public struct EndOfCentralDirectory
    {
        public static readonly byte[] Signature = [0x50, 0x4b, 0x05, 0x06];
        public ushort DiskNr;
        public ushort CentralDirectoryDiskNr;
        public ushort DiskEntries;
        public ushort TotalEntries;
        public uint CentralDirectorySize;
        public uint CentralDirectoryOffset;
        public ushort CommentLength;
        public byte[] Comment;
    }

    /// <summary>
    /// Represents a Central Directory file header.
    /// </summary>
    public struct CentralDirectoryFileHeader
    {
        public static readonly byte[] Signature = [0x50, 0x4b, 0x01, 0x02];
        public ushort VersionMadeBy;
        public ushort VersionNeeded;
        public ushort Flags;
        public CompressionMethod CompressionMethod;
        public ushort FileModificationTime;
        public ushort FileModificationDate;
        public uint Crc32;
        public uint CompressedSize;
        public uint UncompressedSize;
        public ushort FileNameLength;
        public ushort ExtraFieldLength;
        public ushort FileCommentLength;
        public ushort DiskNr;
        public ushort InternalAttribs;
        public uint ExternalAttribs;
        public uint LocalFileHeaderOffset;
        public string FileName;
        public byte[] ExtraField;
        public byte[] FileComment;
    }

    /// <summary>
    /// The compression method with which a file in a zip archive is compressed.
    /// </summary>
    public enum CompressionMethod
    {
        None = 0,
        Deflate = 8,
    }
}
