using Sprache;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TruckLib;
using TruckLib.Sii;
using static Extractor.PathUtils;

namespace Extractor.Deep
{
    internal class SiiPathFinder
    {
        private static readonly HashSet<string> ignorableUnitKeys = [
            "package_version", "compatible_versions", "suitable_for",
            "conflict_with", "truck_y_override",

            // names
            "name", "names", "category", "sign_name", "display_name", "ferry_name",
            "static_lod_name", "board_name", "sign_template_names", "editor_name",
            "default_name", "stamp_name",

            // city
            "city_name", "city_names", "city_name_localized", "short_city_name", 
            "truck_lp_template",

            // curve_model
            "variation", "vegetation", "overlay", "no_stretch_start", "no_stretch_end",

            // vehicle
            "vehicle_brands", "acc_list", "trailer_chains", "info",

            // academy
            "map_title", "map_desc", "intro_cutscene_tag", "start_position_tag",

            // cutscene
            "condition_data", "str_params",

            "color_variant", // model 
            "allowed_vehicle", // traffic logic
            "overlay_schemes", // road
        ];

        private static readonly HashSet<string> ignorableUnitClasses = [
            "cargo_def", "traffic_spawn_condition", "traffic_rule_data", "mail_text", "driver_names",
            "localization_db", "input_device_config", "default_input_config", "default_input_entry",
            "default_setup_entry", "academy_scenario_goal",
        ];

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

            if (unitClass == "license_plate_data" || unitClass == "city_data")
            {
                if (attrib.Key.Contains("def"))
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
                    if (attrib.Key == "sounds" || attrib.Key == "sound_path")
                    {
                        PathFinder.Add(GetSoundPath(str), potentialPaths, []);
                    }
                    else if (attrib.Key == "adr_info_icon" || attrib.Key == "fallback")
                    {
                        var parts = str.Split("|");
                        if (parts.Length == 2)
                            PathFinder.Add(parts[1], potentialPaths, []);
                        else
                            Debugger.Break();
                    }
                    else if (unitClass == "company_permanent" && attrib.Key == "sound")
                    {
                        var parts = str.Split("|");
                        if (parts.Length == 2)
                            PathFinder.Add(parts[1], potentialPaths, []);
                        else
                            Debugger.Break();
                    }
                    else
                    {
                        PathFinder.Add(str, potentialPaths, []);
                    }
                }
            }
        }

        private static string GetSoundPath(string str)
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
            return soundPath;
        }

        private static void ProcessStringAttribute(KeyValuePair<string, dynamic> attrib, 
            string unitClass, HashSet<string> potentialPaths)
        {
            string str = attrib.Value;

            if (unitClass == "ui::text"
                || unitClass == "ui::text_template"
                || unitClass == "ui::text_common"
                || unitClass == "ui_text_bar"
                || (unitClass == "sign_template_text" && attrib.Key == "text")
                || attrib.Key == "offence_message")
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
            else if (attrib.Key == "sound_path" || attrib.Key == "sound_sfx" || attrib.Key == "scene_music")
            {
                PathFinder.Add(GetSoundPath(str), potentialPaths, []);
            }
            else
            {
                PathFinder.Add(str, potentialPaths, []);
            }
        }
    }
}
