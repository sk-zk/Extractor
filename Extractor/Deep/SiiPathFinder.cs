using Sprache;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TruckLib;
using TruckLib.Sii;
using static Extractor.PathUtils;

namespace Extractor.Deep
{
    internal class SiiPathFinder
    {
        private static readonly HashSet<string> ignorableUnitKeys =
            ["category", "sign_name", "display_name", "ferry_name",
            "static_lod_name", "city_names", "city_name_localized", "board_name",
            "vehicle_brands", "package_version", "compatible_versions", 
            "suitable_for", "conflict_with"];

        private static readonly HashSet<string> ignorableUnitClasses =
            ["cargo_def", "traffic_spawn_condition"];

        public static HashSet<string> FindPathsInSii(byte[] fileBuffer, string filePath, IFileSystem fs)
        {
            var magic = Encoding.UTF8.GetString(fileBuffer[0..4]);
            if (!(magic == "SiiN" // regular sii
                || magic == "\uFEFFS" // regular sii with fucking BOM
                || magic == "ScsC" // encrypted sii
                || magic.StartsWith("3nK") // 3nK-encoded sii
                ))
            {
            #if DEBUG
                Console.Error.WriteLine($"Not a sii file: {filePath}");
            #endif
                return [];
            }

            var siiDirectory = GetParent(filePath);
            try
            {
                var sii = SiiFile.Load(fileBuffer, siiDirectory, fs, true);
                return FindPathsInSii(sii);
            }
            catch (ParseException)
            {
                if (!filePath.StartsWith("/ui/template"))
                    Debugger.Break();
                throw;
            }
            catch (Exception)
            {
                Debugger.Break();
                throw;
            }
        }

        private static HashSet<string> FindPathsInSii(SiiFile sii)
        {
            HashSet<string> potentialPaths = new(sii.Includes);

            foreach (var unit in sii.Units)
            {
                foreach (var attrib in unit.Attributes)
                {
                    ProcessSiiUnitAttribute(unit.Class, attrib, potentialPaths);
                }
            }

            return potentialPaths;
        }

        internal static void ProcessSiiUnitAttribute(string unitClass, 
            KeyValuePair<string, dynamic> attrib, HashSet<string> potentialPaths)
        {
            if (ignorableUnitClasses.Contains(unitClass))
                return;

            if (ignorableUnitKeys.Contains(attrib.Key))
                return;

            if (attrib.Value is string)
            {
                ProcessStringAttribute(attrib, unitClass, potentialPaths);
            }
            else if (attrib.Value is IList<dynamic> && attrib.Value[0] is string)
            {
                ProcessArrayAttribute(attrib, unitClass, potentialPaths);
            }
        }

        private static void ProcessArrayAttribute(KeyValuePair<string, dynamic> attrib, string unitClass,
            HashSet<string> potentialPaths)
        {
            IList<dynamic> items = attrib.Value;
            var strings = items.Where(x => x is string).Cast<string>();

            if (unitClass == "license_plate_data")
            {
                if (attrib.Key.StartsWith("def"))
                {
                    foreach (var str in strings)
                    {
                        // Extract paths from img and font tags of the faux-HTML
                        // used in UI strings.
                        var matches = Regex.Matches(str, @"(?:src|face)=(\S*)");
                        foreach (Match match in matches)
                        {
                            PathFinder.Add(match.Groups[1].Value, potentialPaths, []);
                        }
                    }
                }
            }
            else
            {
                foreach (var str in strings)
                {
                    if (attrib.Key == "sounds")
                    {
                        var pipeIdx = str.IndexOf('|');
                        if (pipeIdx == -1)
                            pipeIdx = 0;
                        else
                            pipeIdx += 1;

                        var hashIdx = str.IndexOf('#');
                        if (hashIdx == -1)
                            hashIdx = str.Length;

                        var soundPath = str[pipeIdx..hashIdx];
                        PathFinder.Add(soundPath, potentialPaths, []);
                    }
                    else
                    {
                        PathFinder.Add(str, potentialPaths, []);
                    }
                }
            }
        }

        private static void ProcessStringAttribute(KeyValuePair<string, dynamic> attrib, 
            string unitClass, HashSet<string> potentialPaths)
        {
            if (unitClass == "ui::text"
                                || unitClass == "ui::text_template"
                                || unitClass == "ui_text_bar")
            {
                // Extract paths from img and font tags of the faux-HTML
                // used in UI strings.
                var matches = Regex.Matches(attrib.Value, @"(?:src|face)=(\S*)");
                foreach (Match match in matches)
                {
                    PathFinder.Add(match.Groups[1].Value, potentialPaths, []);
                }
            }
            else if (attrib.Key == "icon" && unitClass != "mod_package")
            {
                // See https://modding.scssoft.com/wiki/Documentation/Engine/Units/trailer_configuration
                // and https://modding.scssoft.com/wiki/Documentation/Engine/Units/accessory_data
                var iconPath = $"/material/ui/accessory/{attrib.Value}";
                if (Path.GetExtension(iconPath) == "")
                {
                    iconPath += ".mat";
                }
                PathFinder.Add(iconPath, potentialPaths, []);
            }
            else
            {
                string s = attrib.Value;
                PathFinder.Add(s, potentialPaths, []);
            }
        }
    }
}
