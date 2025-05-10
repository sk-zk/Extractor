using Extractor;

namespace Extractor.Tests
{
    public class PathUtilsTest
    {
        [Fact]
        public void ReplaceControlChars()
        {
            Assert.Equal("a�b", PathUtils.ReplaceControlChars("a\tb"));
            Assert.Equal("ab�c", PathUtils.ReplaceControlChars("ab\nc"));
            Assert.Equal("abc�", PathUtils.ReplaceControlChars("abc\u200b"));
            Assert.Equal("", PathUtils.ReplaceControlChars(""));
        }

        [Fact]
        public void ReplaceChars()
        {
            Assert.Equal("axixu", PathUtils.ReplaceChars("aeiou", ['e', 'o'], 'x'));
            Assert.Equal("xxxx", PathUtils.ReplaceChars("aaaa", ['a'], 'x'));
            Assert.Equal("aeiou", PathUtils.ReplaceChars("aeiou", ['b', 'c', 'd'], 'x'));
            Assert.Equal("", PathUtils.ReplaceChars("", ['a'], 'x'));
        }

        [Fact]
        public void ReplaceCharsUnambiguously()
        {
            Assert.Equal("ax3Cbcx3Ed", PathUtils.ReplaceCharsUnambiguously("a<bc>d", ['<', '>']));
            Assert.Equal("", PathUtils.ReplaceCharsUnambiguously("", ['x']));
        }

        [Fact]
        public void RemoveInitialSlash()
        {
            Assert.Equal("aaa", PathUtils.RemoveInitialSlash("/aaa"));
            Assert.Equal("a/b/c", PathUtils.RemoveInitialSlash("/a/b/c"));
            Assert.Equal("/", PathUtils.RemoveInitialSlash("/"));
            Assert.Equal("/", PathUtils.RemoveInitialSlash("//"));
            Assert.Equal("", PathUtils.RemoveInitialSlash(""));
        }

        [Fact]
        public void RemoveTrailingSlash()
        {
            Assert.Equal("/aaa", PathUtils.RemoveTrailingSlash("/aaa/"));
            Assert.Equal("/a/b/c", PathUtils.RemoveTrailingSlash("/a/b/c/"));
            Assert.Equal("a/b/c", PathUtils.RemoveTrailingSlash("a/b/c/"));
            Assert.Equal("/", PathUtils.RemoveTrailingSlash("/"));
            Assert.Equal("/", PathUtils.RemoveTrailingSlash("//"));
            Assert.Equal("", PathUtils.RemoveTrailingSlash(""));
        }

        [Fact]
        public void GetParent()
        {
            Assert.Equal("/", PathUtils.GetParent("/hello.sii"));
            Assert.Equal("/hello", PathUtils.GetParent("/hello/world"));
            Assert.Equal("/hello", PathUtils.GetParent("/hello/world/"));
            Assert.Equal("/ä/ö", PathUtils.GetParent("/ä/ö/ü.sii"));
            Assert.Equal("/", PathUtils.GetParent("/hello"));
            Assert.Equal("/", PathUtils.GetParent("/"));
            Assert.Equal("", PathUtils.GetParent(""));
            Assert.Equal("", PathUtils.GetParent("  "));
        }

        [Fact]
        public void Combine()
        {
            Assert.Equal("hello.scs/", PathUtils.Combine("hello.scs", "/"));
            Assert.Equal("hello.scs/world", PathUtils.Combine("hello.scs", "world"));
            Assert.Equal("hello.scs/world", PathUtils.Combine("hello.scs", "/world"));
            Assert.Equal("hello.scs/world/", PathUtils.Combine("hello.scs", "/world/"));
            Assert.Equal("hello.scs/world/foo.sii", PathUtils.Combine("hello.scs", "world/foo.sii"));
            Assert.Equal("hello.scs/world/foo.sii", PathUtils.Combine("hello.scs", "/world/foo.sii"));
            Assert.Equal("/foo/bar", PathUtils.Combine("", "foo/bar"));
        }

        [Fact]
        public void ResemblesPath()
        {
            Assert.True(PathUtils.ResemblesPath("/"));
            Assert.True(PathUtils.ResemblesPath("/def/world"));
            Assert.True(PathUtils.ResemblesPath("def/world"));
            Assert.True(PathUtils.ResemblesPath("/vehicle/truck/bla.pmd"));
            Assert.True(PathUtils.ResemblesPath("vehicle/truck/bla.pmd"));
            Assert.True(PathUtils.ResemblesPath("bla.pmd"));
            Assert.False(PathUtils.ResemblesPath("//"));
            Assert.False(PathUtils.ResemblesPath("aAaAaAa"));
            Assert.False(PathUtils.ResemblesPath(""));
            Assert.False(PathUtils.ResemblesPath("\t"));
            Assert.False(PathUtils.ResemblesPath(null));
        }

        [Fact]
        public void LinesToHashSet()
        {
            var input = "hello\nworld\r\nfoo\nbar";
            var actual = PathUtils.LinesToHashSet(input);
            Assert.Equal(4, actual.Count);
            Assert.Contains("hello", actual);
            Assert.Contains("world", actual);
            Assert.Contains("foo", actual);
            Assert.Contains("bar", actual);
        }

        [Fact]
        public void SanitizePath()
        {
            char[] invalidOnWindows = [
                '\"', '<', '>', '|', ':', '*', '?', 
                '\0', '\u0001', '\u0002', '\u0003', '\u0004', '\u0005', '\u0006', 
                '\a', '\b', '\t', '\n', '\v', '\f', '\r', '\u000e', '\u000f', 
                '\u0010', '\u0011', '\u0012', '\u0013', '\u0014', '\u0015', '\u0016', '\u0017', 
                '\u0018', '\u0019', '\u001a', '\u001b', '\u001c', '\u001d', '\u001e', '\u001f'
                ];

            Assert.Equal("no/changes/äöü.txt", 
                PathUtils.SanitizePath("no/changes/äöü.txt", invalidOnWindows));

            Assert.Equal("x2A_x3F.pmd", 
                PathUtils.SanitizePath("*_?.pmd", invalidOnWindows));

            Assert.Equal("*_?.pmd",
                PathUtils.SanitizePath("*_?.pmd", ['\0'], false));

            Assert.Equal("__/__/__/__/hello.exe", 
                PathUtils.SanitizePath("../../../../hello.exe", invalidOnWindows));

            Assert.Equal("/aux_/hello/LPT1_.sii",
                PathUtils.SanitizePath("/aux/hello/LPT1.sii", invalidOnWindows, true));

            Assert.Equal("/aux/hello/LPT1.sii",
                PathUtils.SanitizePath("/aux/hello/LPT1.sii", ['\0'], false));
        }

        [Fact]
        public void AppendBeforeExtension()
        {
            Assert.Equal("/a/b/c42.txt", PathUtils.AppendBeforeExtension("/a/b/c.txt", "42"));
            Assert.Equal("/a/b/c42", PathUtils.AppendBeforeExtension("/a/b/c", "42"));
        }
    }
}
