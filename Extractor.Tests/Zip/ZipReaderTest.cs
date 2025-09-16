using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Extractor.Zip;

namespace Extractor.Tests.Zip
{
    public class ZipReaderTest
    {
        [Fact]
        public void ExtractFile()
        {
            using var zip = ZipReader.Open("Data/ZipReaderTest/regular.zip");

            Assert.Single(zip.Entries);
            Assert.True(zip.Entries.ContainsKey("foo.txt"));
            var entry = zip.Entries["foo.txt"];
            Assert.Equal("foo.txt", entry.FileName);
            Assert.Equal(44u, entry.CompressedSize);
            Assert.Equal(53u, entry.UncompressedSize);

            using var ms = new MemoryStream();
            zip.GetEntry(entry, ms);
            var actual = ms.ToArray();
            var expected = Encoding.UTF8.GetBytes("it was the best of times, it was the blurst of times?");
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ExtractFileFromCorrupted()
        {
            using var zip = ZipReader.Open("Data/ZipReaderTest/corrupted.zip");

            Assert.Single(zip.Entries);
            Assert.True(zip.Entries.ContainsKey("foo.txt"));
            var entry = zip.Entries["foo.txt"];
            Assert.Equal("foo.txt", entry.FileName);
            Assert.Equal(44u, entry.CompressedSize);
            Assert.Equal(53u, entry.UncompressedSize);

            using var ms = new MemoryStream();
            zip.GetEntry(entry, ms);
            var actual = ms.ToArray();
            var expected = Encoding.UTF8.GetBytes("it was the best of times, it was the blurst of times?");
            Assert.Equal(expected, actual);
        }
    }
}
