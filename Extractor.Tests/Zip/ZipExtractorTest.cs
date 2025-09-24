using Extractor.Zip;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Extractor.Tests.Zip
{
    public class ZipExtractorTest
    {
        [Fact]
        public void GetEntriesToExtract()
        {
            var zip = ZipReader.Open("Data/ZipExtractorTest/test.zip");
            var entries = ZipExtractor.GetEntriesToExtract(zip, ["/"]).
                OrderBy(x => x.FileName).ToList();
            Assert.Equal(3, entries.Count);
            Assert.Equal("bla?.txt", entries[0].FileName);
            Assert.Equal("def/nothing.sii", entries[1].FileName);
            Assert.Equal("hello.sii", entries[2].FileName);
        }

        [Fact]
        public void GetEntriesToExtractWithPathFilter()
        {
            var zip = ZipReader.Open("Data/ZipExtractorTest/test.zip");
            var entries = ZipExtractor.GetEntriesToExtract(zip, ["/def/"]).ToList();
            Assert.Single(entries);
            Assert.Equal("def/nothing.sii", entries[0].FileName);
        }

        [Fact]
        public void DeterminePathSubstitutions()
        {
            PathUtils.ResetNameCounter();
            var zip = ZipReader.Open("Data/ZipExtractorTest/test.zip");
            var entries = ZipExtractor.GetEntriesToExtract(zip, ["/"]);
            var subst = ZipExtractor.DeterminePathSubstitutions(entries);
            Assert.Matches("^F\\d{8}\\.txt$", subst["bla?.txt"]);
        }
    }
}
