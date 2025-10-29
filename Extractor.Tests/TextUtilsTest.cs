using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Extractor;

namespace Extractor.Tests
{
    public class TextUtilsTest
    {
        [Fact]
        public void FindQuotedPaths()
        {
            var input = """
                # "ignorethis.asdf"
                foo : bar {
                    // relative path
                    a: "relative.txt"  //"ignore this"

                    // absolute path
                    b: "/absolute/path.txt"

                    /* ignore "/this"      
                       as "well" */

                    // not a file path; ignore
                    c: "ignore \"this\""

                    // missing end quote; does exist in the wild
                    d: "no/end/quote

                    // path containing # and /* */
                    e: "/?#_/*_:*/>_*"          
                }
                // "/ignore/this"
                """;
            var positions = TextUtils.FindQuotedPaths(input);
            var actual = positions.Select(x => input[x]).ToList();
            List<string> expected = [
                "relative.txt",
                "/absolute/path.txt",
                "no/end/quote",
                "/?#_/*_:*/>_*",
                ];
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ReplaceRenamedPathsInSii()
        {
            var input = """
                foo : bar {
                    a: "/a/b"
                    b: 3.14
                @include "42.sui"
                    c: "/a/c.txt"
                    d: "/a/b/c.txt"
                }
                """;
            var substitutions = new Dictionary<string, string>()
            {
                { "/a/b", "/c/d" },
                { "42.sui", "727.sui" },
            };
            var expected = """
                foo : bar {
                    a: "/c/d"
                    b: 3.14
                @include "727.sui"
                    c: "/a/c.txt"
                    d: "/a/b/c.txt"
                }
                """;
            var (actual, modified) = TextUtils.ReplaceRenamedPaths(input, substitutions);
            Assert.Equal(expected, actual);
            Assert.True(modified);
        }

        [Fact]
        public void ReplaceRenamedPathsInSiiNoModification()
        {
            var input = """
                foo : bar {
                    a: "/q/b"
                    b: 3.14
                @include "foo.sui"
                    c: "/a/c.txt"
                    d: "/a/b/c.txt"
                }
                """;
            var substitutions = new Dictionary<string, string>()
            {
                { "/a/b", "/c/d" },
                { "3.14", "7.27" },
            };
            var expected = input;
            var (actual, modified) = TextUtils.ReplaceRenamedPaths(input, substitutions);
            Assert.Equal(expected, actual);
            Assert.False(modified);
        }

        [Fact]
        public void WildcardStringToRegex()
        {
            var input = "a*b?(c).p??";
            var regex = TextUtils.WildcardStringToRegex(input);
            Assert.Equal(@"^a.*b.\(c\)\.p..$", regex.ToString());
            Assert.Matches(regex, "axxbx(c).pmd");
            Assert.Matches(regex, "axxbx(c).ppd");
            Assert.Matches(regex, "abx(c).pmd");
            Assert.DoesNotMatch(regex, "axxbxX(c).pmd");
            Assert.DoesNotMatch(regex, "ab(c).pmg");
            Assert.DoesNotMatch(regex, "Xaxxbx(c).pmd");
            Assert.DoesNotMatch(regex, "axxbx(c).pmgX");
            Assert.DoesNotMatch(regex, "axxbx(c).jpg");
        }
    }
}
