using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TruckLib.Models;
using TruckLib.Sii;

namespace Extractor
{
    internal class PathSubstitution
    {
        internal static (bool Modified, byte[] Buffer) SubstitutePathsInTextFormats(byte[] buffer,
            Dictionary<string, string> substitutions, string extension)
        {
            var wasModified = false;

            var isSii = extension == ".sii";
            var isOtherTextFormat = extension == ".sui" || extension == ".mat";

            if (isSii || isOtherTextFormat)
            {
                if (isSii)
                {
                    buffer = SiiFile.Decode(buffer);
                }

                var content = Encoding.UTF8.GetString(buffer);
                (content, wasModified) = TextUtils.ReplaceRenamedPaths(content, substitutions);

                buffer = Encoding.UTF8.GetBytes(content);
            }

            return (wasModified, buffer);
        }

        internal static (bool Modified, byte[] Buffer) SubstitutePathsInTobj(byte[] buffer,
            Dictionary<string, string> substitutions)
        {
            var wasModified = false;

            var tobj = Tobj.Load(buffer);
            for (int i = 0; i < tobj.TexturePaths.Count; i++)
            {
                if (substitutions.TryGetValue(tobj.TexturePaths[i], out var substitution))
                {
                    tobj.TexturePaths[i] = substitution;
                    wasModified = true;
                }
            }

            if (wasModified)
            {
                using var ms = new MemoryStream();
                using var w = new BinaryWriter(ms);
                tobj.Serialize(w);
                buffer = ms.ToArray();
            }

            return (wasModified, buffer);
        }
    }
}
