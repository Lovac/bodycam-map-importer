// File names, paths and naming rules for map packages.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BodycamMapInstaller.Core
{
    public static class PackagePaths
    {
        public static readonly string[] AllModes = { "HP", "TDM", "DM", "VS", "GG", "BB", "WM" };   // 22 Sep: every hostable wrapper mode (GM_Maps/*)
        public static readonly int[] RealThumbLengths = { 6, 10, 13, 14, 17, 22, 30, 32 };
        public const string DaPrefix = "/Game/UI/MetaData/";
        public const string TexturePrefix = "/Game/UI/Textures/Maps/";
        public const string LevelTokenLobby = "Lobby";

        public static readonly string[] GameRowNames = { "Trenches", "Russian", "WornHouse", "Wornhouse", "Airsoft", "Airsoft1", "Hospital", "BombHouse", "Pool", "Rome", "Paintball", "Logistics", "CQB", "Tumblewood", "Backrooms", "Airport", "OilRig", "OulRig", "RussianImperfecterTest", "Pit", "Assylum", "Village", "ShootingRange" };
        public static readonly string[] WeatherRowNames = { "RussianBuilding", "PublicPool", "WeatherSystem", "Main_Trenches_Level_Design" };

        public static readonly string[] GameLevelNames = { "Main_Trenches_Level_Design", "RussianBuilding", "RussianBuilding_rs_hmika", "WornHouse", "Airsoft", "Airport", "Hospital", "BombHouse", "PublicPool", "Rome", "Paintball", "Logistics", "CQB", "Tumblewood", "TheBackrooms", "OilRig" };

        static readonly Regex IdRx = new Regex("^[A-Za-z][A-Za-z0-9]{1,22}$", RegexOptions.CultureInvariant);
        static readonly Regex TextureRx = new Regex("^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant);

        public static bool IsSafeEntryName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 200) return false;
            if (name.IndexOf('\\') >= 0 || name.IndexOf(':') >= 0 || name.StartsWith("/")) return false;
            foreach (string part in name.Split('/'))
                if (part == ".." || part == ".") return false;
            foreach (char c in name)
                if (c < 0x20 || c > 0x7E) return false;
            return true;
        }

        public static bool IsSafeFileName(string name)
        {
            return IsSafeEntryName(name) && name.IndexOf('/') < 0;
        }

        public static bool IsMode(string mode)
        {
            return Array.IndexOf(AllModes, mode) >= 0;
        }

        public static string ModeFolder(string mode)
        {
            switch (mode) { case "HP": return "Hardpoint"; case "TDM": return "TeamDeathmatch"; case "DM": return "DeathMatch"; case "VS": return "Versus"; case "GG": return "GunGame"; case "BB": return "BodyBomb"; case "WM": return "Wingman"; }
            throw new ArgumentException("mode " + mode);
        }

        public static string ModePrefix(string mode)
        {
            if (!IsMode(mode)) throw new ArgumentException("mode " + mode);
            return mode + "_";
        }

        public static string ModeLongName(string mode)
        {
            switch (mode) { case "HP": return "Hardpoint"; case "TDM": return "Team Deathmatch"; case "DM": return "Deathmatch"; case "VS": return "Versus"; case "GG": return "Gun Game"; case "BB": return "Body Bomb"; case "WM": return "Wingman"; }
            return mode;
        }

        public static string PreferredModeLongName(IList<string> modes)
        {
            if (modes != null)
                foreach (string m in new string[] { "TDM", "DM", "HP" })
                    if (modes.Contains(m)) return ModeLongName(m);
            return ModeLongName("TDM");
        }

        static readonly Regex PakSuffixRx = new Regex(@"(_\d+)?_P$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static string FriendlyPakName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return fileName;
            string s = fileName;
            if (s.EndsWith(".off", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            if (s.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            string stem = s;
            s = PakSuffixRx.Replace(s, "");
            if (s.Length > 2 && s.StartsWith("zz", StringComparison.Ordinal) && char.IsUpper(s[2])) s = s.Substring(2);
            else if (s.Length > 1 && s[0] == 'z' && char.IsUpper(s[1])) s = s.Substring(1);
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '_' || c == '-' || c == '.') { sb.Append(' '); continue; }
                if (i > 0)
                {
                    char p = s[i - 1];
                    bool lowerToUpper = char.IsLower(p) && char.IsUpper(c);
                    bool acronymEnd = char.IsUpper(p) && char.IsUpper(c) && i + 1 < s.Length && char.IsLower(s[i + 1]);
                    bool letterDigit = char.IsLetter(p) && char.IsDigit(c);
                    if (lowerToUpper || acronymEnd || letterDigit) sb.Append(' ');
                }
                sb.Append(c);
            }
            string r = Regex.Replace(sb.ToString(), " +", " ").Trim();
            return r.Length > 0 ? r : stem;
        }

        public static string ContentPakName(string id) { return "z" + id + "_9_P.pak"; }
        public static string ModesPakName(string id) { return "z" + id + "_Modes_9_P.pak"; }
        public static string WrapperPath(string id, string mode) { return "GM_Maps/" + ModeFolder(mode) + "/" + ModePrefix(mode) + id; }
        public static string DaPath(string id, string mode) { return "UI/MetaData/DA_" + id + "_" + mode; }
        public static string DaPackage(string id, string mode) { return DaPrefix + "DA_" + id + "_" + mode; }
        public static string DaObject(string id, string mode) { return "DA_" + id + "_" + mode; }

        public static string ThumbName(string id)
        {
            string b = "T_UI_Map_" + id;
            foreach (int n in RealThumbLengths)
                if (n >= b.Length) return b.PadRight(n, '0');
            return null;
        }

        public static bool IsLegalTextureName(string id, string textureName)
        {
            if (textureName == null || !TextureRx.IsMatch(textureName)) return false;
            if (!textureName.StartsWith("T_UI_Map_" + id, StringComparison.Ordinal)) return false;
            return Array.IndexOf(RealThumbLengths, textureName.Length) >= 0;
        }

        public static string WeatherObjectName(string weatherDa)
        {
            if (string.IsNullOrEmpty(weatherDa)) return null;
            string last = weatherDa.Substring(weatherDa.LastIndexOf('/') + 1);
            int dot = last.IndexOf('.');
            return dot >= 0 ? last.Substring(dot + 1) : last;
        }

        public static string WeatherPackage(string weatherDa)
        {
            if (string.IsNullOrEmpty(weatherDa)) return null;
            int dot = weatherDa.LastIndexOf('.');
            int slash = weatherDa.LastIndexOf('/');
            return dot > slash ? weatherDa.Substring(0, dot) : weatherDa;
        }

        public static IList<string> IdProblems(string id, IEnumerable<string> modes, IEnumerable<string> takenNames)
        {
            List<string> p = new List<string>();
            if (id == null || !IdRx.IsMatch(id))
            {
                p.Add("The map name \"" + id + "\" must start with a letter and use 2 to 23 letters or digits.");
                return p;
            }
            if (id.IndexOf("lobby", StringComparison.OrdinalIgnoreCase) >= 0) p.Add("The map name \"" + id + "\" contains \"lobby\", which the game treats as its lobby.");
            foreach (string g in GameRowNames)
                if (string.Equals(g, id, StringComparison.OrdinalIgnoreCase)) p.Add("The map name \"" + id + "\" is already a Bodycam map.");
            foreach (string g in WeatherRowNames)
                if (string.Equals(g, id, StringComparison.OrdinalIgnoreCase)) p.Add("The map name \"" + id + "\" is already a Bodycam weather row.");
            if (modes != null)
                foreach (string m in modes)
                {
                    if (!IsMode(m)) continue;
                    string full = ModePrefix(m) + id;
                    foreach (string level in GameLevelNames)
                        if (full.IndexOf(level, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            p.Add("The map name \"" + id + "\" contains \"" + level + "\": the scoreboard and map voting would mix it up with that Bodycam map.");
                            break;
                        }
                }
            if (takenNames != null)
                foreach (string t in takenNames)
                    if (string.Equals(t, id, StringComparison.OrdinalIgnoreCase)) { p.Add("The map name \"" + id + "\" is already taken by \"" + t + "\"."); break; }
            return p;
        }
    }
}
