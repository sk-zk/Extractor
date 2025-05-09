using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TruckLib.HashFs;

namespace Extractor.Tests
{
    public class HashFsExtractorTest
    {
        [Fact]
        public void DeterminePathSubstitutions()
        {
            List<string> paths = [
                "/bla?.txt",
                "/def/country.sii"
                ];
            var actual = HashFsExtractor.DeterminePathSubstitutions(paths);
            Assert.Single(actual);
            Assert.Equal("/blax3F.txt", actual["/bla?.txt"]);
        }

        [Fact]
        public void GetPathsToExtract()
        {
            var reader = HashFsReader.Open("Data/HashFsExtractorTest/test.scs");
            List<string> expected = [
                "/automat/25/25af2491ef54222a.mat",
                "/automat/37/3775c3a12b425cbb.mat",
                "/automat/4a/4ab2bc7f5cf359be.mat",
                "/automat/56/565c15ccb866c86c.mat",
                "/automat/5c/5c41a9995a1f1aa1.mat",
                "/automat/61/611fd82f2507cf09.mat",
                "/automat/86/867047c129118d22.mat",
                "/automat/88/885fe2be016ed070.mat",
                "/automat/92/925af071d5f7f458.mat",
                "/automat/9b/9bf23deeb5fcd770.mat",
                "/automat/b1/b1c164866a0b4350.mat",
                "/automat/c1/c143ee535353e7bb.mat",
                "/automat/c6/c6a769a6d9dfcec9.mat",
                "/automat/d6/d6964521d9de0b38.mat",
                "/automat/ff/ff03ea7e5bb88d72.mat",
                "/def/world/curve_model.osm_proto.sii",
                "/material/special/placeholder_01.tobj",
                "/model2/building/osm_proto/generated/__default.mat",
                "/model2/building/osm_proto/highway_10m.pmd",
                "/model2/building/osm_proto/highway_10m.pmg",
                "/model2/building/osm_proto/highway_1m_path.pmd",
                "/model2/building/osm_proto/highway_1m_path.pmg",
                "/model2/building/osm_proto/highway_5m.pmd",
                "/model2/building/osm_proto/highway_5m.pmg",
                "/model2/building/osm_proto/highway_cycleway.tobj",
                "/model2/building/osm_proto/highway_footway.tobj",
                "/model2/building/osm_proto/highway_motorway.tobj",
                "/model2/building/osm_proto/highway_path.tobj",
                "/model2/building/osm_proto/highway_pedestrian.tobj",
                "/model2/building/osm_proto/highway_platform.tobj",
                "/model2/building/osm_proto/highway_primary.tobj",
                "/model2/building/osm_proto/highway_residential.tobj",
                "/model2/building/osm_proto/highway_secondary.tobj",
                "/model2/building/osm_proto/highway_service.tobj",
                "/model2/building/osm_proto/highway_tertiary.tobj",
                "/model2/building/osm_proto/highway_track.tobj",
                "/model2/building/osm_proto/highway_unclassified.tobj",
                "/model2/building/osm_proto/rail_normal.tobj",
                "/model2/building/osm_proto/rail_siding.tobj",
                "/model2/building/osm_proto/rail.pmd",
                "/model2/building/osm_proto/rail.pmg",
                ];
            var actual = HashFsExtractor.GetPathsToExtract(reader, ["/"], (_) => { });
            actual.Sort();
            Assert.Equal(expected, actual);

            expected = [
                "/def/world/curve_model.osm_proto.sii",
                "/material/special/placeholder_01.tobj",
                ];
            actual = HashFsExtractor.GetPathsToExtract(reader, ["/def", "/material"], (_) => { });
            actual.Sort();
            Assert.Equal(expected, actual);
        }
    }
}
