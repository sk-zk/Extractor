using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Extractor
{
    public class BinaryUtils
    {
        /// <summary>
        /// Searches for the last occurrence of a byte sequence in a stream.
        /// </summary>
        /// <param name="reader">The <see cref="BinaryReader"/> to read from.</param>
        /// <param name="bytes">The byte sequence to search for.</param>
        /// <param name="limit">The maximum number of bytes to check from the end of the stream.
        /// Defaults to -1, which means that there is no maximum.</param>
        /// <returns>The offset from the start of the last occurrence of the sequence in the stream,
        /// or -1 if the sequence was not found.</returns>
        public static long FindBytesBackwards(BinaryReader reader, byte[] bytes, int limit = -1)
        {
            long endPos = limit < 0 
                ? 0 
                : Math.Max(0, reader.BaseStream.Length - limit - 1);
            int seqIdx = bytes.Length - 1;
            long offset = -1;

            reader.BaseStream.Seek(-bytes.Length, SeekOrigin.End);
            while (reader.BaseStream.Position > endPos)
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

        public static void CopyStream(Stream input, Stream output, long bytes)
        {
            var buffer = new byte[32768];
            int read;
            while (bytes > 0 && (read = input.Read(buffer, 0, Math.Min(buffer.Length, (int)bytes))) > 0)
            {
                output.Write(buffer, 0, read);
                bytes -= read;
            }
        }
    }
}
