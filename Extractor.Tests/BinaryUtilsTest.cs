using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Extractor;

namespace Extractor.Tests
{
    public class BinaryUtilsTest
    {
        [Fact]
        public void FindBytesBackwards()
        {
            byte[] bytes = [
                0x00, 0x22, 0x11, 0x22,
                0x00, 0x11, 0x00, 0x00,
                0x11, 0x22, 0x33, 0x44,
                0x22, 0x11, 0x00, 0x22,
                ];
            using var ms = new MemoryStream(bytes);
            using var reader = new BinaryReader(ms);
            Assert.Equal(-1, BinaryUtils.FindBytesBackwards(reader, [0x55, 0x66]));
            Assert.Equal(8, BinaryUtils.FindBytesBackwards(reader, [0x11, 0x22]));
        }
    }
}
