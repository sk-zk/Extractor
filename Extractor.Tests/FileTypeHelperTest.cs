using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Extractor.Deep;

namespace Extractor.Tests
{
    public class FileTypeHelperTest
    {
        [Fact]
        public void InferPmd()
        {
            var bytes = File.ReadAllBytes("Data/FileTypeHelperTest/sample.pmd");
            Assert.Equal(FileType.Pmd, FileTypeHelper.Infer(bytes));
        }

        [Fact]
        public void InferPmdNoParts()
        {
            var bytes = File.ReadAllBytes("Data/FileTypeHelperTest/sample_no_parts.pmd");
            Assert.Equal(FileType.Pmd, FileTypeHelper.Infer(bytes));
        }

        [Fact]
        public void InferPma()
        {
            var bytes = File.ReadAllBytes("Data/FileTypeHelperTest/sample.pma");
            Assert.Equal(FileType.Pma, FileTypeHelper.Infer(bytes));
        }

        [Fact]
        public void InferPmc()
        {
            var bytes = File.ReadAllBytes("Data/FileTypeHelperTest/sample.pmc");
            Assert.Equal(FileType.Pmc, FileTypeHelper.Infer(bytes));
        }
    }
}
