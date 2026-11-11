using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace VillagePlebs
{
    internal static class Looks
    {
        private const string SkinT = "vp_skin_t", HairT = "vp_hair_t", HairL = "vp_hair_l";
        public const string EiLookKey = "vp_eilook", EiTattooKey = "vp_eitattoos", EiUnderwearKey = "vp_eiunderwear";

        private static readonly Color Skin0 = new Color(1f, 1f, 1f), Skin1 = new Color(0.32f, 0.23f, 0.17f);
        private static readonly Color Hair0 = new Color(1f, 0.86f, 0.55f), Hair1 = new Color(0.45f, 0.24f, 0.11f);
        private const float HairMin = 0.1f, HairMax = 1f;

        public static List<ItemDrop> Hairs() => Styles("Hair");
        public static List<ItemDrop> Beards() => Styles("Beard");

        private static List<ItemDrop> Styles(string prefix)
        {
            if (ObjectDB.instance == null) return new List<ItemDrop>();
            return ObjectDB.instance.GetAllItems(ItemDrop.ItemData.ItemType.Customization, prefix)
                .Where(i => i != null && !i.name.EndsWith("None"))
                .OrderBy(i => Number(i.name)).ThenBy(i => i.name).ToList();
        }

        private static int Number(string name) { string d = new string(name.Where(char.IsDigit).ToArray()); return int.TryParse(d, out int n) ? n : 999; }

        public static int Model(Component c) => Companion.Zdo(c)?.GetInt(ZDOVars.s_modelIndex, 0) ?? 0;
        public static int Hair(Component c) => Companion.Zdo(c)?.GetInt(ZDOVars.s_hairItem, 0) ?? 0;
        public static int Beard(Component c) => Companion.Zdo(c)?.GetInt(ZDOVars.s_beardItem, 0) ?? 0;
        public static float SkinTone(Component c) => Companion.Zdo(c)?.GetFloat(SkinT, 0.3f) ?? 0.3f;
        public static float HairTone(Component c) => Companion.Zdo(c)?.GetFloat(HairT, 0.5f) ?? 0.5f;
        public static float HairShade(Component c) => Companion.Zdo(c)?.GetFloat(HairL, 0.8f) ?? 0.8f;
        public static string EiLook(Component c) => Companion.Zdo(c)?.GetString(EiLookKey, "") ?? "";
        public static string EiTattoos(Component c) => Companion.Zdo(c)?.GetString(EiTattooKey, "") ?? "";
        public static string EiUnderwear(Component c) => Companion.Zdo(c)?.GetString(EiUnderwearKey, "") ?? "";

        public static void SetEiLook(Humanoid c, string look, string tattoos, string underwear)
        {
            ZDO z = Companion.Zdo(c);
            z.Set(EiLookKey, look ?? "");
            z.Set(EiTattooKey, tattoos ?? "");
            z.Set(EiUnderwearKey, underwear ?? "");
            EinherjarLook.Apply(c.gameObject, look);
            EinherjarLook.ApplyTattoos(c.gameObject, tattoos);
            EinherjarLook.ApplyUnderwear(c.gameObject, underwear);
        }

        public static bool HasEiLook(string look, string tattoos, string underwear) => !string.IsNullOrEmpty(look) || !string.IsNullOrEmpty(tattoos) || !string.IsNullOrEmpty(underwear);

        public static string StyleName(int hash, List<ItemDrop> styles)
        {
            if (hash == 0) return Localization.instance.Localize("$menu_none");
            int i = styles.FindIndex(s => s.name.GetStableHashCode() == hash);
            return i < 0 ? "?" : VillagePlebs.Shared.Translations.T("villagepleb_looks_style_of", i + 1, styles.Count);
        }

        private static VisEquipment Vis(Humanoid c) => c.GetComponent<VisEquipment>();

        public static void SetModel(Humanoid c, int model)
        {
            ZDO z = Companion.Zdo(c);
            if (z == null || z.GetInt(ZDOVars.s_modelIndex, 0) == model) return;
            z.Set(ZDOVars.s_modelIndex, model);
            if (model == 1) { z.Set(ZDOVars.s_beardItem, 0); z.Set(ZDOVars.s_noBeard, true); }
        }

        private static readonly HarmonyLib.AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> Instances =
            HarmonyLib.AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances");

        public static void Remake(Humanoid c)
        {
            ZNetView view = c.GetComponent<ZNetView>();
            ZDO zdo = view?.GetZDO();
            if (zdo == null || ZNetScene.instance == null) return;
            Brain.Forget(c);
            view.ResetZDO();
            Instances(ZNetScene.instance).Remove(zdo);
            UnityEngine.Object.Destroy(c.gameObject);
            VillagePlebsPlugin.Instance?.Note("A companion was remade with a different body");
        }

        public static void SetModelNow(Humanoid c, int model)
        {
            Vis(c)?.SetModel(model);
            if (model == 1) SetBeard(c, 0);
        }

        public static void SetHair(Humanoid c, int hash)
        {
            ZDO z = Companion.Zdo(c);
            Vis(c)?.SetHairItem(hash);
            z.Set(ZDOVars.s_hairItem, hash);
            z.Set(ZDOVars.s_noHair, hash == 0);
        }

        public static void SetBeard(Humanoid c, int hash)
        {
            ZDO z = Companion.Zdo(c);
            Vis(c)?.SetBeardItem(hash);
            z.Set(ZDOVars.s_beardItem, hash);
            z.Set(ZDOVars.s_noBeard, hash == 0);
        }

        public static void SetSkin(Humanoid c, float t)
        {
            Companion.Zdo(c).Set(SkinT, t);
            Vis(c)?.SetSkinColor(SkinColor(t));
        }

        public static void SetHairColor(Humanoid c, float tone, float shade)
        {
            ZDO z = Companion.Zdo(c);
            z.Set(HairT, tone);
            z.Set(HairL, shade);
            Vis(c)?.SetHairColor(HairColor(tone, shade));
        }

        public static Vector3 SkinColor(float t) => Utils.ColorToVec3(Color.Lerp(Skin0, Skin1, t));
        public static Vector3 HairColor(float tone, float shade) => Utils.ColorToVec3(Color.Lerp(Hair0, Hair1, tone) * Mathf.Lerp(HairMin, HairMax, shade));

        public static float SkinOf(Vector3 colour)
        {
            Vector3 from = Utils.ColorToVec3(Skin0), line = Utils.ColorToVec3(Skin1) - from;
            return Mathf.Clamp01(Vector3.Dot(colour - from, line) / Mathf.Max(0.0001f, line.sqrMagnitude));
        }

        public static Vector2 HairOf(Vector3 colour)
        {
            Vector2 best = new Vector2(0.5f, 0.8f);
            float bestDistance = float.MaxValue;
            for (int i = 0; i <= 50; i++)
                for (int j = 0; j <= 50; j++)
                {
                    float distance = (HairColor(i / 50f, j / 50f) - colour).sqrMagnitude;
                    if (distance < bestDistance) { bestDistance = distance; best = new Vector2(i / 50f, j / 50f); }
                }
            return best;
        }

        public static int Step(int current, List<ItemDrop> styles, int by)
        {
            var hashes = new List<int> { 0 };
            hashes.AddRange(styles.Select(s => s.name.GetStableHashCode()));
            int i = Mathf.Max(0, hashes.IndexOf(current));
            return hashes[(i + by + hashes.Count) % hashes.Count];
        }

        internal struct Look
        {
            public int Model, Hair, Beard;
            public float Skin, HairTone, HairShade;
            public string EiLook;
            public string EiTattoos;
            public string EiUnderwear;
        }

        public static Look Of(Component c) =>
            new Look { Model = Model(c), Hair = Hair(c), Beard = Beard(c), Skin = SkinTone(c), HairTone = HairTone(c), HairShade = HairShade(c), EiLook = EiLook(c), EiTattoos = EiTattoos(c), EiUnderwear = EiUnderwear(c) };

        public static Look Random()
        {
            List<ItemDrop> hairs = Hairs(), beards = Beards();
            var l = new Look { Model = UnityEngine.Random.Range(0, 2), Skin = UnityEngine.Random.Range(0f, 0.9f), HairTone = UnityEngine.Random.value, HairShade = UnityEngine.Random.Range(0.35f, 1f) };
            if (hairs.Count > 0) l.Hair = hairs[UnityEngine.Random.Range(0, hairs.Count)].name.GetStableHashCode();
            if (l.Model == 0 && beards.Count > 0 && UnityEngine.Random.value < 0.85f) l.Beard = beards[UnityEngine.Random.Range(0, beards.Count)].name.GetStableHashCode();
            return l;
        }

        public static Look Like(Player p)
        {
            ZDO pz = p != null ? p.GetComponent<ZNetView>()?.GetZDO() : null;
            if (pz == null) return new Look { Skin = 0.3f, HairTone = 0.5f, HairShade = 0.8f };
            Vector2 hair = HairOf(pz.GetVec3(ZDOVars.s_hairColor, Vector3.one));
            return new Look
            {
                Model = pz.GetInt(ZDOVars.s_modelIndex, 0), Hair = pz.GetInt(ZDOVars.s_hairItem, 0), Beard = pz.GetInt(ZDOVars.s_beardItem, 0),
                Skin = SkinOf(pz.GetVec3(ZDOVars.s_skinColor, Vector3.one)), HairTone = hair.x, HairShade = hair.y,
            };
        }

        public static void Set(Humanoid c, Look l, bool fresh = false)
        {
            if (fresh) SetModelNow(c, l.Model);
            SetSkin(c, l.Skin);
            SetHairColor(c, l.HairTone, l.HairShade);
            SetHair(c, l.Hair);
            SetBeard(c, l.Model == 0 ? l.Beard : 0);
            if (HasEiLook(l.EiLook, l.EiTattoos, l.EiUnderwear)) SetEiLook(c, l.EiLook, l.EiTattoos, l.EiUnderwear);
            if (!fresh) SetModel(c, l.Model);
        }

        public static void Randomise(Humanoid c, bool fresh) => Set(c, Random(), fresh);
    }

    // Its picture: a still of the Looks preview, taken when its looks are applied (or the Looks tab first shows them), kept on disk.
    internal static class Portraits
    {
        private static readonly Dictionary<long, Texture2D> Shots = new Dictionary<long, Texture2D>();
        private static readonly HashSet<long> Missing = new HashSet<long>();

        private static string File(long id) => System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "VillagePlebs", "Portraits", id + ".png");

        public static Texture Get(long id)
        {
            if (Shots.TryGetValue(id, out Texture2D tex) && tex != null) return tex;
            if (id == 0L || Missing.Contains(id)) return null;
            tex = Load(File(id));
            if (tex == null) { Missing.Add(id); return null; }
            Shots[id] = tex;
            return tex;
        }

        public static void Set(long id, Texture2D tex)
        {
            if (id == 0L || tex == null) return;
            if (Shots.TryGetValue(id, out Texture2D old) && old != null && old != tex) UnityEngine.Object.Destroy(old);
            Shots[id] = tex;
            Missing.Remove(id);
            try
            {
                MethodInfo encode = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")?.GetMethod("EncodeToPNG", new[] { typeof(Texture2D) });
                if (!(encode?.Invoke(null, new object[] { tex }) is byte[] png)) return;
                string file = File(id);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                System.IO.File.WriteAllBytes(file, png);
            }
            catch (Exception e) { VillagePlebsPlugin.Instance?.Warn("Could not save a companion's portrait: " + e.Message); }
        }

        private static Texture2D Load(string file)
        {
            try
            {
                if (!System.IO.File.Exists(file)) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "vp_portrait" };
                MethodInfo load = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                if (load != null && (bool)load.Invoke(null, new object[] { tex, System.IO.File.ReadAllBytes(file) })) return tex;
                UnityEngine.Object.Destroy(tex);
            }
            catch (Exception e) { VillagePlebsPlugin.Instance?.Warn("Could not load a companion's portrait: " + e.Message); }
            return null;
        }

        public static void Stop()
        {
            foreach (Texture2D t in Shots.Values) if (t != null) UnityEngine.Object.Destroy(t);
            Shots.Clear();
            Missing.Clear();
        }
    }
}

namespace VillagePlebs
{
    [HarmonyLib.HarmonyPatch(typeof(VisEquipment), "UpdateBaseModel")]
    internal static class VisEquipment_UpdateBaseModel
    {
        private static readonly HarmonyLib.AccessTools.FieldRef<VisEquipment, int> Current = HarmonyLib.AccessTools.FieldRefAccess<VisEquipment, int>("m_currentModelIndex");
        private static readonly System.Collections.Generic.HashSet<VisEquipment> Drawn = new System.Collections.Generic.HashSet<VisEquipment>();

        private static bool Prefix(VisEquipment __instance)
        {
            if (!Companion.Is(__instance)) return true;
            int wanted = Companion.Zdo(__instance).GetInt(ZDOVars.s_modelIndex, 0);
            if (Drawn.Add(__instance)) { if (Drawn.Count > 64) Drawn.RemoveWhere(v => v == null); return true; }
            if (wanted == Current(__instance)) return true;
            Looks.Remake(__instance.GetComponent<Humanoid>());
            return false;
        }
    }
}
