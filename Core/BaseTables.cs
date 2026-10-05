// The game's map-list tables, embedded in the exe, and a check that the installed game still matches them.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace BodycamMapInstaller.Core
{
    public sealed class BaseCheck
    {
        public enum Status { Match, Mismatch, PaksMissing }
        public Status Result;
        public List<string> Details = new List<string>();
    }

    public sealed class BaseTables
    {
        static readonly string[][] Known = {
            new string[] { "UI/Menus/Play/Cards/Data/DT_UI_HardpointMaps.uasset", "2385", "a4d036d126325efb6f8b8c7a511799e3905321db", "324e10011223a9781e687ac05bd4f5d1", "pakchunk24-Windows.pak" },
            new string[] { "UI/Menus/Play/Cards/Data/DT_UI_HardpointMaps.uexp", "153", "4a6c774b13e89dc553107588897c4e26e6f1b6bf", "fcf07a374da3672a51613146ea596028", "pakchunk24-Windows.pak" },
            new string[] { "UI/Menus/Play/Cards/Data/DT_UI_TeamDeathmatchMaps.uasset", "3261", "777bda1bdf6d60c5b86d3b82cd0f0c89ddf3aa13", "1f80fe0599d0ecd101bd7c174545c6c1", "pakchunk24-Windows.pak" },
            new string[] { "UI/Menus/Play/Cards/Data/DT_UI_TeamDeathmatchMaps.uexp", "243", "fb15f7b4e894c03729d812649311601a9dd3345c", "1ede300e0760e70c5114e15a84f86ee5", "pakchunk24-Windows.pak" },
            new string[] { "UI/Menus/Play/Cards/Data/DT_UI_DeathmatchMaps.uasset", "3448", "bca7d4c08bd08fd6bf6b061717b10902476d062a", "d9cd7c91eff0495b51a67ec3dc792295", "pakchunk24-Windows.pak" },
            new string[] { "UI/Menus/Play/Cards/Data/DT_UI_DeathmatchMaps.uexp", "258", "d18eee3abab14cd60b8d282039bb93dd23a402b1", "e3913ef9731940f94e9aba820bf4b35b", "pakchunk24-Windows.pak" },
            new string[] { "UI/Menus/Play/Cards/Data/DT_UI_VersusMaps.uasset", "3085", "64b5e0190c0987fb25e9de6ade7f3fd0336685e1", "2e8ba79e2dd2f46eee30a15d04ced642", "pakchunk24-Windows.pak" },
            new string[] { "UI/Menus/Play/Cards/Data/DT_UI_VersusMaps.uexp", "228", "502f2368f614e047e3ab733de0220b591e9e969e", "1d02c86ff2262762b7e7db9ded705dec", "pakchunk24-Windows.pak" },
            new string[] { "UI/Menus/Play/Cards/Data/DT_UI_GungameMaps.uasset", "3097", "1ceb6e496fc076515564970302b7dc28488cfaf3", "414426c43d38a352a14b4c83ea277d7a", "pakchunk24-Windows.pak" },
            new string[] { "UI/Menus/Play/Cards/Data/DT_UI_GungameMaps.uexp", "228", "6c24337e1f60e2f9a988f1c87e25f900779205b0", "35ab7448d916770b2a44f7545f3406af", "pakchunk24-Windows.pak" },
            new string[] { "UI/Menus/Play/Cards/Data/DT_UI_BodybombMaps.uasset", "2945", "644952765fcd9d91ec0c8462e002eeecd2d9c99b", "0bde9a8be684b6efbfaaa228460e2a7d", "pakchunk24-Windows.pak" },
            new string[] { "UI/Menus/Play/Cards/Data/DT_UI_BodybombMaps.uexp", "213", "365fc5e611f1b09d0dae8a3090a8e1c9e0c970f5", "a63bf6fec7e329834743a45a5a527ba8", "pakchunk24-Windows.pak" },
            new string[] { "UI/Menus/Play/Cards/Data/DT_UI_WingmanMaps.uasset", "2531", "b662ed6e4bf07fa4757662aec5bf490748dc3544", "0b421955f150df20558ff826fa22a3f5", "pakchunk24-Windows.pak" },
            new string[] { "UI/Menus/Play/Cards/Data/DT_UI_WingmanMaps.uexp", "168", "a67693539d87622d5022eac3c941af48b32c1046", "a0ba617ea524a1ce72752dfdcbb9f510", "pakchunk24-Windows.pak" },
            new string[] { "GM/WeatherManaer/DT_MapWeatherConfig.uasset", "3012", "d9947c5abb8d097a21cffe5f5fbaf1494423a2db", "520f979a6f14ab47864bc9edbf14603d", "pakchunk26-Windows.pak" },
            new string[] { "GM/WeatherManaer/DT_MapWeatherConfig.uexp", "183", "bcdb0feda2275be32723465075675ea4e9d3ef73", "ba9ac1503fc5338a7afac1ce31a547f8", "pakchunk26-Windows.pak" },
        };

        public static readonly string[] RelPaths = MakeRelPaths();

        static string[] MakeRelPaths()
        {
            string[] r = new string[Known.Length];
            for (int i = 0; i < Known.Length; i++) r[i] = Known[i][0];
            return r;
        }

        readonly Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        static BaseTables embedded;

        public static BaseTables Embedded()
        {
            if (embedded != null) return embedded;
            Assembly a = typeof(BaseTables).Assembly;
            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string n in a.GetManifestResourceNames()) names[n.Replace('\\', '/')] = n;
            BaseTables t = new BaseTables();
            foreach (string rel in RelPaths)
            {
                string real;
                if (!names.TryGetValue("BaseTables/" + rel, out real)) throw new InvalidOperationException("the installer was built without its base tables (resource BaseTables/" + rel + ")");
                using (Stream s = a.GetManifestResourceStream(real)) t.files[rel] = Util.ReadAll(s);
            }
            IList<string> bad = t.FingerprintProblems();
            if (bad.Count > 0) throw new InvalidOperationException("embedded base tables differ from the measured ones: " + string.Join("; ", new List<string>(bad).ToArray()));
            embedded = t;
            return t;
        }

        public static BaseTables FromDirectory(string contentDir)
        {
            BaseTables t = new BaseTables();
            foreach (string rel in RelPaths) t.files[rel] = File.ReadAllBytes(Path.Combine(contentDir, rel.Replace('/', '\\')));
            return t;
        }

        public static BaseTables FromFiles(IDictionary<string, byte[]> byRelPath)
        {
            BaseTables t = new BaseTables();
            foreach (string rel in RelPaths) t.files[rel] = byRelPath[rel];
            return t;
        }

        public byte[] Get(string relPath) { return files[relPath]; }

        public CookedTable Table(string stem)
        {
            return new CookedTable(files[stem + ".uasset"], files[stem + ".uexp"]);
        }

        public IList<string> FingerprintProblems()
        {
            List<string> bad = new List<string>();
            foreach (string[] k in Known)
            {
                byte[] b;
                if (!files.TryGetValue(k[0], out b)) { bad.Add(k[0] + " missing"); continue; }
                if (b.Length.ToString() != k[1] || Util.Md5Hex(b) != k[3]) bad.Add(k[0] + " md5 " + Util.Md5Hex(b));
            }
            return bad;
        }

        public HashSet<string> RowNames(string stem)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TableRow r in DataTableMerger.ReadRows(Table(stem))) set.Add(r.Name);
            return set;
        }

        public bool NameTablesContain(string name)
        {
            foreach (string stem in DataTableMerger.AllTables)
            {
                CookedPackageHeader h = CookedPackageHeader.ParseLenient(files[stem + ".uasset"]);
                foreach (KeyValuePair<string, byte[]> n in h.Names)
                    if (string.Equals(n.Key, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public BaseCheck CheckAgainst(GamePakIndex index)
        {
            BaseCheck c = new BaseCheck();
            if (index == null || index.PakCount == 0)
            {
                c.Result = BaseCheck.Status.PaksMissing;
                c.Details.Add("no pakchunk*-Windows.pak in the Paks folder");
                return c;
            }
            c.Result = BaseCheck.Status.Match;
            foreach (string[] k in Known)
            {
                string pak;
                PakEntryInfo e = index.Winner(k[0], out pak);
                if (e == null)
                {
                    c.Result = BaseCheck.Status.Mismatch;
                    c.Details.Add(k[0] + " is in no game pak");
                    continue;
                }
                string sha = Util.Hex(e.Sha1);
                bool sizeOk = e.UncompressedSize.ToString() == k[1];
                bool shaOk = sha == k[2] || (e.Method == 0 && files.ContainsKey(k[0]) && sha == Util.Sha1Hex(files[k[0]]));
                if (!sizeOk || !shaOk)
                {
                    c.Result = BaseCheck.Status.Mismatch;
                    c.Details.Add(k[0] + " in " + pak + ": size " + e.UncompressedSize + " sha1 " + sha + " (expected " + k[1] + " / " + k[2] + ")");
                }
            }
            return c;
        }
    }
}
