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
            Assert.Equal("foo.txt", zip.Entries[0].FileName);
            Assert.Equal(44u, zip.Entries[0].CompressedSize);
            Assert.Equal(53u, zip.Entries[0].UncompressedSize);

            using var ms = new MemoryStream();
            zip.GetEntry(zip.Entries[0], ms);
            var actual = ms.ToArray();
            var expected = Encoding.UTF8.GetBytes("it was the best of times, it was the blurst of times?");
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ExtractFileFromCorrupted()
        {
            using var zip = ZipReader.Open("Data/ZipReaderTest/corrupted.zip");

            Assert.Single(zip.Entries);
            Assert.Equal("foo.txt", zip.Entries[0].FileName);
            Assert.Equal(44u, zip.Entries[0].CompressedSize);
            Assert.Equal(53u, zip.Entries[0].UncompressedSize);

            using var ms = new MemoryStream();
            zip.GetEntry(zip.Entries[0], ms);
            var actual = ms.ToArray();
            var expected = Encoding.UTF8.GetBytes("it was the best of times, it was the blurst of times?");
            Assert.Equal(expected, actual);
        }
    }
}
