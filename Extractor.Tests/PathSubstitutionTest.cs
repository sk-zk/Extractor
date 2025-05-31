using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TruckLib.Models;

namespace Extractor.Tests
{
    public class PathSubstitutionTest
    {
        [Fact]
        public void SubstitutePathsInTobj()
        {
            var before = File.ReadAllBytes("Data/PathSubstitutionTest/subst_before.tobj");

            var (wasModified, actual) = PathSubstitution.SubstitutePathsInTobj(before, new()
            {
                { "/hello:/asdf?.dds", "/hellox3A/asdfx3F.dds" }
            });
            var expected = File.ReadAllBytes("Data/PathSubstitutionTest/subst_after.tobj");

            Assert.True(wasModified);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void SubstitutePathsInTobjNoModifictaion()
        {
            var before = File.ReadAllBytes("Data/PathSubstitutionTest/subst_before.tobj");

            var (wasModified, actual) = PathSubstitution.SubstitutePathsInTobj(before, new() { });

            Assert.False(wasModified);
            Assert.Equal(before, actual);
        }
    }
}
