using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Extractor.Deep;
using TruckLib.Sii;

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

        [Fact]
        public void ConstructLicensePlateMatPaths()
        {
            var sii = SiiFile.Open("Data/SiiPathFinderTest/norway_license_plates.sii");

            var paths = new PotentialPaths();
            SiiPathFinder.ConstructLicensePlateMatPaths("norway", paths, sii);

            Assert.Contains("/material/ui/lp/norway/front.mat", paths);
            Assert.Contains("/material/ui/lp/norway/police_front.mat", paths);
            Assert.Contains("/material/ui/lp/norway/police_rear.mat", paths);
            Assert.Contains("/material/ui/lp/norway/rear.mat", paths);
            Assert.Contains("/material/ui/lp/norway/rigid.mat", paths);
            Assert.Contains("/material/ui/lp/norway/trailer.mat", paths);
            Assert.Contains("/material/ui/lp/norway/truck_front.mat", paths);
            Assert.Contains("/material/ui/lp/norway/truck_rear.mat", paths);
        }
    }
}
