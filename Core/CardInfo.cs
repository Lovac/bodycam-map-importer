// card.json: read, check and write.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace BodycamMapInstaller.Core
{
    public sealed class CardInfo
    {
        public const int CurrentFormat = 1;
        public int Format;
        public string Id, DisplayName, Description, Version, Author, LevelPackage, Thumbnail, MenuTexture, WeatherDa, GameBuild, Homepage;
        public string[] Modes, Replaces, Credits;

        static readonly Regex VersionRx = new Regex(@"^\d{1,9}\.\d{1,9}\.\d{1,9}$", RegexOptions.CultureInvariant);
        static readonly Regex PackageRx = new Regex(@"^/Game/Sandbox/[A-Za-z0-9_/]+$", RegexOptions.CultureInvariant);
        static readonly Regex GamePathRx = new Regex(@"^/Game/[A-Za-z0-9_/]+(\.[A-Za-z0-9_]+)?$", RegexOptions.CultureInvariant);

        static readonly string[] Required = { "format", "id", "display_name", "description", "version", "author", "modes", "level_package", "thumbnail", "menu_texture", "weather_da" };

        public static CardInfo Parse(byte[] utf8Json)
        {
            JsonObject o;
            try { o = Json.ParseObject(utf8Json); }
            catch (InvalidDataException) { throw new PackageException("has a card.json the installer cannot read"); }
            object fv;
            if (!o.TryGetValue("format", out fv) || !(fv is double) || (double)fv != Math.Floor((double)fv))
                throw new PackageException("has no package format number in its card.json");
            CardInfo c = new CardInfo();
            c.Format = (int)(double)fv;
            if (c.Format > CurrentFormat) throw new PackageException("needs a newer installer (package format " + c.Format + ")");
            if (c.Format < 1) throw new PackageException("has no package format number in its card.json");
            foreach (string k in Required)
                if (!o.Has(k)) throw new PackageException("has no \"" + k + "\" in its card.json");
            c.Id = Text(o, "id", false);
            c.DisplayName = Text(o, "display_name", false);
            c.Description = Text(o, "description", false);
            c.Version = Text(o, "version", false);
            c.Author = Text(o, "author", false);
            c.LevelPackage = Text(o, "level_package", false);
            c.Thumbnail = Text(o, "thumbnail", false);
            c.MenuTexture = Text(o, "menu_texture", false);
            c.WeatherDa = Text(o, "weather_da", true);
            c.GameBuild = o.Has("game_build") ? Text(o, "game_build", true) : null;
            c.Homepage = o.Has("homepage") ? Text(o, "homepage", true) : null;
            c.Modes = List(o, "modes");
            c.Replaces = o.Has("replaces") ? List(o, "replaces") : new string[0];
            c.Credits = o.Has("credits") ? List(o, "credits") : new string[0];
            return c;
        }

        static string Text(JsonObject o, string key, bool nullable)
        {
            object v = o[key];
            if (v == null && nullable) return null;
            string s = v as string;
            if (s == null) throw new PackageException("has \"" + key + "\" in its card.json in a form the installer does not read");
            return s;
        }

        static string[] List(JsonObject o, string key)
        {
            List<object> a = o[key] as List<object>;
            if (a == null) throw new PackageException("has \"" + key + "\" in its card.json in a form the installer does not read");
            List<string> r = new List<string>();
            foreach (object x in a)
            {
                string s = x as string;
                if (s == null) throw new PackageException("has \"" + key + "\" in its card.json in a form the installer does not read");
                r.Add(s);
            }
            return r.ToArray();
        }

        public byte[] ToJson()
        {
            JsonOut o = new JsonOut();
            o.Set("format", Format == 0 ? CurrentFormat : Format)
             .Set("id", Id).Set("display_name", DisplayName).Set("description", Description).Set("version", Version).Set("author", Author)
             .Set("modes", Modes ?? new string[0]).Set("level_package", LevelPackage).Set("thumbnail", Thumbnail ?? "thumb.png")
             .Set("menu_texture", MenuTexture).Set("weather_da", WeatherDa);
            if (Replaces != null && Replaces.Length > 0) o.Set("replaces", Replaces);
            if (GameBuild != null) o.Set("game_build", GameBuild);
            if (Homepage != null) o.Set("homepage", Homepage);
            if (Credits != null && Credits.Length > 0) o.Set("credits", Credits);
            return Json.ToBytes(o);
        }

        public string TextureName
        {
            get { return MenuTexture != null && MenuTexture.StartsWith(PackagePaths.TexturePrefix, StringComparison.Ordinal) ? MenuTexture.Substring(PackagePaths.TexturePrefix.Length) : null; }
        }

        public string TextureRelPath
        {
            get { string t = TextureName; return t == null ? null : "UI/Textures/Maps/" + t; }
        }

        public string ModesText
        {
            get
            {
                List<string> n = new List<string>();
                if (Modes != null) foreach (string m in Modes) n.Add(PackagePaths.ModeLongName(m));
                return string.Join(", ", n.ToArray());
            }
        }

        public string FirstModeLongName
        {
            get { return PackagePaths.PreferredModeLongName(Modes); }
        }

        static bool Latin1(string s)
        {
            foreach (char c in s) if (c > 0xFF || c < 0x20) return false;
            return true;
        }

        public IList<string> FieldProblems()
        {
            List<string> p = new List<string>();
            if (DisplayName == null || DisplayName.Length < 1 || DisplayName.Length > 32 || !Latin1(DisplayName)) p.Add("The display name must be 1 to 32 plain characters.");
            if (Description == null || Description.Length > 120 || !Latin1(Description)) p.Add("The description must be at most 120 plain characters.");
            if (Version == null || !VersionRx.IsMatch(Version)) p.Add("The version \"" + Version + "\" must look like 1.0.0.");
            if (Author == null || Author.Length < 1 || Author.Length > 64) p.Add("The author must be 1 to 64 characters.");
            if (Modes == null || Modes.Length == 0) p.Add("The map declares no game mode.");
            else
            {
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string m in Modes)
                {
                    if (!PackagePaths.IsMode(m)) p.Add("The game mode \"" + m + "\" is not one of HP, TDM, DM.");
                    else if (!seen.Add(m)) p.Add("The game mode " + m + " is listed twice.");
                }
            }
            if (LevelPackage == null || !PackageRx.IsMatch(LevelPackage)) p.Add("The level \"" + LevelPackage + "\" must be under /Game/Sandbox/.");
            if (Thumbnail != "thumb.png") p.Add("The photo must be thumb.png.");
            string tex = TextureName;
            if (tex == null || !PackagePaths.IsLegalTextureName(Id ?? "", tex))
                p.Add("The card photo name \"" + MenuTexture + "\" must be /Game/UI/Textures/Maps/T_UI_Map_" + Id + " with a length the game uses (for example " + (PackagePaths.ThumbName(Id ?? "") ?? "shorter id") + ").");
            if (WeatherDa != null && !GamePathRx.IsMatch(WeatherDa)) p.Add("The weather \"" + WeatherDa + "\" must be a /Game/ path.");
            if (Replaces != null)
                foreach (string r in Replaces)
                {
                    if (!PackagePaths.IsSafeFileName(r) || !r.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)) p.Add("\"" + r + "\" in replaces must be a .pak file name.");
                    else if (r.StartsWith("pakchunk", StringComparison.OrdinalIgnoreCase)) p.Add("\"" + r + "\" in replaces is a Bodycam game file.");
                    else if (string.Equals(r, MenuPakBuilder.PakName, StringComparison.OrdinalIgnoreCase)) p.Add("\"" + r + "\" in replaces is the installer's own menu file.");
                    else if (Id != null && (string.Equals(r, PackagePaths.ContentPakName(Id), StringComparison.OrdinalIgnoreCase) || string.Equals(r, PackagePaths.ModesPakName(Id), StringComparison.OrdinalIgnoreCase)))
                        p.Add("\"" + r + "\" in replaces is one of this map's own paks.");
                }
            return p;
        }

        public static int CompareVersions(string a, string b)
        {
            string[] x = (a ?? "").Split('.'), y = (b ?? "").Split('.');
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                long xi, yi;
                bool okx = long.TryParse(i < x.Length ? x[i] : "0", out xi), oky = long.TryParse(i < y.Length ? y[i] : "0", out yi);
                if (!okx || !oky) return string.CompareOrdinal(a, b);
                if (xi != yi) return xi < yi ? -1 : 1;
            }
            return 0;
        }
    }
}
