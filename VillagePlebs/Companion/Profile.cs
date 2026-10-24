using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace VillagePlebs
{
    internal class Profile
    {
        public long Id, World;
        public string Name = "Rádvar";
        public int Order, Style = 1, Retreat = 30, Kills, Jobs, Radius = 30, Model;
        public int Ration = 12, DutyDest;
        public bool Frugal = true;
        public bool Potions = true, Protect = true, UseJev = true, Loot = true, Pantry, Chatty = true, Stow = true, AutoHome = true, HomeSet, Armory = true;
        public Vector3 HomeSpot;
        public string MissionText = "", DutiesText = "";
        public int PickList = (int)VillagePlebs.Loot.Default;
        public Vector3 Skin = Vector3.one, HairColor = Vector3.one;
        public int Hair, Beard;
        public float SkinT = 0.3f, HairT = 0.5f, HairL = 0.8f;
        public string BrLook = "";
        public float BrMuscle = -1f;
        public string BrTattoos = "";
        public string Skills = "";
        public long MmoXp;
        public string Kept = "";
        public string JournalText = "", TallyText = "";
        public int Since = -1;
        public bool HasBed;
        public Vector3 Bed, LastSeen;
        public bool Dead, HasGrave;
        public double DiedAt;
        public Vector3 DiedPos;

        private const string Key = "vp_companions";

        public static long WorldId => ZNet.instance != null ? ZNet.instance.GetWorldUID() : 0L;

        private static string _readJson;
        private static List<Profile> _read = new List<Profile>();

        public static List<Profile> All(Player p)
        {
            if (p == null || !p.m_customData.TryGetValue(Key, out string json) || string.IsNullOrEmpty(json)) return new List<Profile>();
            if (!ReferenceEquals(json, _readJson) && json != _readJson)
            {
                var list = new List<Profile>();
                try { foreach (JToken t in JArray.Parse(json)) list.Add(FromJson((JObject)t)); }
                catch (Exception e) { VillagePlebsPlugin.Instance?.Warn("Could not read the companion profiles: " + e.Message); }
                _readJson = json;
                _read = list;
            }
            return _read.Select(x => (Profile)x.MemberwiseClone()).ToList();
        }

        public static List<Profile> Here(Player p) => All(p).Where(x => x.World == WorldId).ToList();

        public static Profile Find(Player p, long id) => Here(p).FirstOrDefault(x => x.Id == id);

        public static void Save(Player p, Profile profile) => Save(p, new[] { profile });

        public static void Save(Player p, IEnumerable<Profile> profiles)
        {
            List<Profile> all = All(p);
            foreach (Profile profile in profiles)
            {
                int at = all.FindIndex(x => x.Id == profile.Id);
                if (at >= 0) all[at] = profile; else all.Add(profile);
            }
            Write(p, all);
        }

        public static void Forget(Player p, long id)
        {
            List<Profile> all = All(p);
            if (all.RemoveAll(x => x.Id == id) > 0) Write(p, all);
        }

        private static void Write(Player p, List<Profile> all)
        {
            string json = new JArray(all.Select(x => (JToken)x.ToJson())).ToString(Newtonsoft.Json.Formatting.None);
            if (p.m_customData.TryGetValue(Key, out string had) && had == json) return;
            p.m_customData[Key] = json;
            _readJson = json;
            _read = all.Select(x => (Profile)x.MemberwiseClone()).ToList();
        }

        public static Profile Of(Humanoid c)
        {
            ZDO z = Companion.Zdo(c);
            BookData book = Book.Of(c);
            return new Profile
            {
                Id = Companion.IdOf(c), World = WorldId, Name = Companion.NameOf(c),
                Order = z.GetInt(Keys.Order, 0), Style = z.GetInt(Keys.Style, 1), Retreat = z.GetInt(Keys.Retreat, 30), Kills = z.GetInt(Keys.Kills, 0),
                Jobs = z.GetInt(Keys.Jobs, 0), Radius = z.GetInt(Keys.Radius, 30),
                Potions = z.GetBool(Keys.Potions, true), Protect = z.GetBool(Keys.Protect, true), UseJev = z.GetBool(Keys.UseJev, true),
                Model = z.GetInt(ZDOVars.s_modelIndex, 0), Skin = z.GetVec3(ZDOVars.s_skinColor, Vector3.one), HairColor = z.GetVec3(ZDOVars.s_hairColor, Vector3.one),
                Hair = z.GetInt(ZDOVars.s_hairItem, 0), Beard = z.GetInt(ZDOVars.s_beardItem, 0),
                SkinT = Looks.SkinTone(c), HairT = Looks.HairTone(c), HairL = Looks.HairShade(c), BrLook = Looks.BrLook(c), BrMuscle = Looks.BrMuscle(c), BrTattoos = Looks.BrTattoos(c), Skills = Skill.Text(c), MmoXp = EpicMmo.Xp(c),
                HasBed = z.GetBool(Keys.HasBed, false), Bed = z.GetVec3(Keys.BedPos, Vector3.zero), LastSeen = c.transform.position,
                HasGrave = z.GetBool(Grave.HasKey, false), DiedPos = z.GetVec3(Grave.PosKey, Vector3.zero), Loot = z.GetBool(VillagePlebs.Loot.Key, true), Kept = z.GetString(Companion.KeptKey, ""), JournalText = book.JournalText, TallyText = book.TallyText, Since = z.GetInt(Journal.SinceKey, -1), Pantry = z.GetBool(Work.PantryKey, false), HomeSet = z.GetBool(Work.HomeSetKey, false), HomeSpot = z.GetVec3(Work.HomeSpotKey, Vector3.zero), MissionText = book.Missions, DutiesText = z.GetString(Duties.Key, ""), Frugal = z.GetBool(Food.FrugalKey, true), Ration = z.GetInt(Work.RationKey, 12), DutyDest = z.GetInt(Duties.DestKey, 0), Stow = z.GetBool(Work.StowKey, true), AutoHome = z.GetBool(Following.AutoHomeKey, true), Chatty = z.GetBool(Talk.ChattyKey, true), Armory = z.GetBool(VillagePlebs.Armory.Key, true), PickList = z.GetInt(VillagePlebs.Loot.ListKey, (int)VillagePlebs.Loot.Default),
            };
        }

        public void ApplyTo(Humanoid c)
        {
            ZDO z = Companion.Zdo(c);
            z.Set(Keys.Id, Id);
            z.Set(Keys.Name, Name);
            z.Set(Keys.Order, Order); z.Set(Keys.Style, Style); z.Set(Keys.Retreat, Retreat); z.Set(Keys.Kills, Kills);
            z.Set(Keys.Jobs, Jobs); z.Set(Keys.Radius, Radius);
            z.Set(Keys.Potions, Potions); z.Set(Keys.Protect, Protect); z.Set(Keys.UseJev, UseJev);
            z.Set(Keys.HasBed, HasBed); z.Set(Keys.BedPos, Bed);
            z.Set(Skill.Key, Skills ?? "");
            if (MmoXp > 0L) z.Set(EpicMmo.XpKey, MmoXp);
            z.Set(Grave.HasKey, HasGrave); z.Set(Grave.PosKey, DiedPos);
            z.Set(VillagePlebs.Loot.Key, Loot); z.Set(VillagePlebs.Loot.ListKey, PickList); z.Set(Work.PantryKey, Pantry); z.Set(Work.HomeSetKey, HomeSet); z.Set(Work.HomeSpotKey, HomeSpot); z.Set(Duties.Key, DutiesText ?? ""); z.Set(Food.FrugalKey, Frugal); z.Set(Work.RationKey, Ration); z.Set(Duties.DestKey, DutyDest); z.Set(Work.StowKey, Stow); z.Set(Journal.SinceKey, Since); z.Set(Following.AutoHomeKey, AutoHome); z.Set(Talk.ChattyKey, Chatty); z.Set(VillagePlebs.Armory.Key, Armory);
            VisEquipment vis = c.GetComponent<VisEquipment>();
            if (vis == null) return;
            vis.SetModel(Model);
            vis.SetSkinColor(Skin);
            vis.SetHairColor(HairColor);
            z.Set("vp_skin_t", SkinT); z.Set("vp_hair_t", HairT); z.Set("vp_hair_l", HairL);
            Looks.SetHair(c, Hair);
            Looks.SetBeard(c, Beard);
            if (!string.IsNullOrEmpty(BrLook) || !string.IsNullOrEmpty(BrTattoos)) Looks.SetBrLook(c, BrLook, BrMuscle, BrTattoos);
        }

        private static JArray V(Vector3 v) => new JArray(Math.Round(v.x, 3), Math.Round(v.y, 3), Math.Round(v.z, 3));
        private static Vector3 V(JToken t) => t is JArray a && a.Count == 3 ? new Vector3((float)a[0], (float)a[1], (float)a[2]) : Vector3.zero;

        public JObject ToJson() => new JObject
        {
            ["id"] = Id.ToString(CultureInfo.InvariantCulture), ["world"] = World.ToString(CultureInfo.InvariantCulture), ["name"] = Name,
            ["order"] = Order, ["style"] = Style, ["retreat"] = Retreat, ["kills"] = Kills, ["jobs"] = Jobs, ["radius"] = Radius,
            ["potions"] = Potions, ["protect"] = Protect, ["jev"] = UseJev,
            ["model"] = Model, ["skin"] = V(Skin), ["haircolor"] = V(HairColor), ["hair"] = Hair, ["beard"] = Beard,
            ["skint"] = SkinT, ["hairt"] = HairT, ["hairl"] = HairL, ["brlook"] = BrLook ?? "", ["brmuscle"] = BrMuscle, ["brtattoos"] = BrTattoos ?? "", ["skills"] = Skills ?? "", ["mmoxp"] = MmoXp.ToString(CultureInfo.InvariantCulture),
            ["hasbed"] = HasBed, ["bed"] = V(Bed), ["seen"] = V(LastSeen),
            ["dead"] = Dead, ["diedat"] = DiedAt, ["diedpos"] = V(DiedPos), ["grave"] = HasGrave, ["loot"] = Loot, ["pick"] = PickList, ["pantry"] = Pantry, ["homeset"] = HomeSet, ["homespot"] = V(HomeSpot), ["missions"] = MissionText ?? "", ["duties"] = DutiesText ?? "", ["frugal"] = Frugal, ["ration"] = Ration, ["dutydest"] = DutyDest, ["stow"] = Stow, ["autohome"] = AutoHome, ["kept"] = Kept ?? "", ["journal"] = JournalText ?? "", ["tally"] = TallyText ?? "", ["since"] = Since, ["chatty"] = Chatty, ["armory"] = Armory,
        };

        private static Profile FromJson(JObject o) => new Profile
        {
            Id = long.Parse((string)o["id"], CultureInfo.InvariantCulture), World = long.Parse((string)o["world"], CultureInfo.InvariantCulture),
            Name = (string)o["name"] ?? "Rádvar",
            Order = (int?)o["order"] ?? 0, Style = (int?)o["style"] ?? 1, Retreat = (int?)o["retreat"] ?? 30, Kills = (int?)o["kills"] ?? 0,
            Jobs = (int?)o["jobs"] ?? 0, Radius = (int?)o["radius"] ?? 30,
            Potions = (bool?)o["potions"] ?? true, Protect = (bool?)o["protect"] ?? true, UseJev = (bool?)o["jev"] ?? true,
            Model = (int?)o["model"] ?? 0, Skin = V(o["skin"]), HairColor = V(o["haircolor"]), Hair = (int?)o["hair"] ?? 0, Beard = (int?)o["beard"] ?? 0,
            SkinT = (float?)o["skint"] ?? 0.3f, HairT = (float?)o["hairt"] ?? 0.5f, HairL = (float?)o["hairl"] ?? 0.8f, BrLook = (string)o["brlook"] ?? "", BrMuscle = (float?)o["brmuscle"] ?? -1f, BrTattoos = (string)o["brtattoos"] ?? "", Skills = (string)o["skills"] ?? "",
            MmoXp = long.TryParse((string)o["mmoxp"] ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out long xp) ? xp : 0L,
            HasBed = (bool?)o["hasbed"] ?? false, Bed = V(o["bed"]), LastSeen = V(o["seen"]),
            Dead = (bool?)o["dead"] ?? false, DiedAt = (double?)o["diedat"] ?? 0, DiedPos = V(o["diedpos"]),
            HasGrave = (bool?)o["grave"] ?? false, Loot = (bool?)o["loot"] ?? true, Kept = (string)o["kept"] ?? "", JournalText = (string)o["journal"] ?? "", TallyText = (string)o["tally"] ?? "", Since = (int?)o["since"] ?? -1, Pantry = (bool?)o["pantry"] ?? false, HomeSet = (bool?)o["homeset"] ?? false, HomeSpot = V(o["homespot"]), MissionText = (string)o["missions"] ?? "", DutiesText = (string)o["duties"] ?? "", Frugal = (bool?)o["frugal"] ?? true, Ration = (int?)o["ration"] ?? 12, DutyDest = (int?)o["dutydest"] ?? 0, Stow = (bool?)o["stow"] ?? true, AutoHome = (bool?)o["autohome"] ?? true, Chatty = (bool?)o["chatty"] ?? true, Armory = (bool?)o["armory"] ?? true, PickList = (int?)o["pick"] ?? (int)VillagePlebs.Loot.Default,
        };
    }
}
