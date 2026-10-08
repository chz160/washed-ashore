using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace WashedAshore.Fish.Editor
{
    /// <summary>
    /// Slot #23 (approved by f-td and f-qa): the footprint pins bind the CONTENT of the record's surfaceSigns.footprints,
    /// not the whole brief file, so a revision that leaves every footprint unchanged needs no re-import (the whole-file brief
    /// guard stays in FishShippedAssetsTests). One canonical form, used by the importer and the tests: UTF-8, no whitespace,
    /// object keys sorted ordinally, numbers as InvariantCulture round-trip ("R"), every field and array of every row.
    /// footprintsNote is a sibling of footprints and is not read by the renderer, so it is not hashed.
    /// </summary>
    public static class FishFootprintHash
    {
        /// <summary>sha256 (hex) of the canonical surfaceSigns.footprints of the targets file at a project-relative path.</summary>
        public static string Of(string projectRelativePath) =>
            OfJson(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), projectRelativePath)));

        /// <summary>sha256 (hex) of the canonical surfaceSigns.footprints in a targets JSON text.</summary>
        public static string OfJson(string targetsJson)
        {
            var j = (Dictionary<string, object>)BellsBendData.Json.Parse(targetsJson);
            var foot = ((Dictionary<string, object>)j["surfaceSigns"])["footprints"];
            using (var h = System.Security.Cryptography.SHA256.Create())
                return System.BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(Canon(foot)))).Replace("-", "").ToLowerInvariant();
        }

        public static string Canon(object o)
        {
            switch (o)
            {
                case null: return "null";
                case string s: return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                case bool b: return b ? "true" : "false";
                case Dictionary<string, object> d:
                    return "{" + string.Join(",", d.Keys.OrderBy(k => k, System.StringComparer.Ordinal).Select(k => Canon(k) + ":" + Canon(d[k]))) + "}";
                case List<object> l: return "[" + string.Join(",", l.Select(Canon)) + "]";
                default: return System.Convert.ToDouble(o, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture);
            }
        }
    }
}
