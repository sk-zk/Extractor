using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Extractor.PathUtils;

namespace Extractor.Deep
{
    /// <summary>
    /// Represents a set of paths which may exist in the archive.
    /// </summary>
    public class PotentialPaths : HashSet<string>
    {
        public PotentialPaths() : base() { }

        public PotentialPaths(IEnumerable<string> collection) : base(collection) { }

        /// <summary>
        /// Adds a string to the set if it is a path and it has not yet been visited.
        /// </summary>
        /// <param name="str">The string.</param>
        /// <param name="visited">The set of paths which have been visited so far.</param>
        public void Add(string str, HashSet<string> visited)
        {
            if (visited.Contains(str) || !ResemblesPath(str))
            {
                return;
            }

            EnsureHasInitialSlash(ref str);

            if (Add(str))
            {
                AddPathVariants(str, visited);
            }
        }

        /// <summary>
        /// Adds a path and common variant paths (e.g., related material/tobj/dds files),
        /// without consulting any external visited set. Caller should de-duplicate later.
        /// </summary>
        /// <param name="path">The path to add.</param>
        public void AddWithVariants(string path)
        {
            if (!ResemblesPath(path))
                return;

            EnsureHasInitialSlash(ref path);

            if (!Add(path))
                return;

            var extension = Path.GetExtension(path);

            if (extension == ".pmd")
            {
                Add(Path.ChangeExtension(path, ".pmg"));
                Add(Path.ChangeExtension(path, ".pmc"));
                Add(Path.ChangeExtension(path, ".pma"));
                Add(Path.ChangeExtension(path, ".ppd"));
            }
            else if (extension == ".pmg")
            {
                Add(Path.ChangeExtension(path, ".pmd"));
                Add(Path.ChangeExtension(path, ".pmc"));
                Add(Path.ChangeExtension(path, ".pma"));
                Add(Path.ChangeExtension(path, ".ppd"));
            }
            else if (extension == ".bank")
            {
                Add(Path.ChangeExtension(path, ".bank.guids"));
            }
            else if (extension == ".mat")
            {
                Add(Path.ChangeExtension(path, ".tobj"));
                Add(Path.ChangeExtension(path, ".dds"));
            }
            else if (extension == ".tobj")
            {
                Add(Path.ChangeExtension(path, ".mat"));
                Add(Path.ChangeExtension(path, ".dds"));
            }
            else if (extension == ".dds")
            {
                Add(Path.ChangeExtension(path, ".mat"));
                Add(Path.ChangeExtension(path, ".tobj"));
            }
        }

        private void AddPathVariants(string path, HashSet<string> visited)
        {
            var extension = Path.GetExtension(path);

            if (extension == ".pmd")
            {
                Add(Path.ChangeExtension(path, ".pmg"), visited);
                Add(Path.ChangeExtension(path, ".pmc"), visited);
                Add(Path.ChangeExtension(path, ".pma"), visited);
                Add(Path.ChangeExtension(path, ".ppd"), visited);
            }
            else if (extension == ".pmg")
            {
                Add(Path.ChangeExtension(path, ".pmd"), visited);
                Add(Path.ChangeExtension(path, ".pmc"), visited);
                Add(Path.ChangeExtension(path, ".pma"), visited);
                Add(Path.ChangeExtension(path, ".ppd"), visited);
            }
            else if (extension == ".bank")
            {
                Add(Path.ChangeExtension(path, ".bank.guids"), visited);
            }
            else if (extension == ".mat")
            {
                Add(Path.ChangeExtension(path, ".tobj"), visited);
                Add(Path.ChangeExtension(path, ".dds"), visited);
            }
            else if (extension == ".tobj")
            {
                Add(Path.ChangeExtension(path, ".mat"), visited);
                Add(Path.ChangeExtension(path, ".dds"), visited);
            }
            else if (extension == ".dds")
            {
                Add(Path.ChangeExtension(path, ".mat"), visited);
                Add(Path.ChangeExtension(path, ".tobj"), visited);
            }
        }
    }
}
