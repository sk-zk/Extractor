using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Extractor.Deep;

namespace Extractor.Tests.Deep
{
    public class SiiPathFinderTest
    {
        [Fact]
        public void ProcessSiiSoundAttribute()
        {
            PotentialPaths actual = [];
            SiiPathFinder.ProcessSiiUnitAttribute("accessory_interior_data",
                new("sounds", new string[] {
                    "air_warning|/def/vehicle/truck/foo/air_warning.soundref",
                    "system_warning1|/sound/truck/foo/bar.bank#interior/system_warning1",
                }),
                actual);
            PotentialPaths expected = [
                "/def/vehicle/truck/foo/air_warning.soundref",
                "/sound/truck/foo/bar.bank",
                "/sound/truck/foo/bar.bank.guids"
                ];
            Assert.Equal(expected, actual);
        }
    }
}
