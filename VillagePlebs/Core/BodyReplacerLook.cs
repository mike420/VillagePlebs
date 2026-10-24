using System;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace VillagePlebs
{
    internal static class BodyReplacerLook
    {
        private const string Guid = "MagicMike.BodyReplacer";
        private const int ApiVersion = 2;

        private static bool _looked;
        private static Func<int, bool, bool> _replaces;
        private static Func<int, int[]> _hairStyleIds;
        private static Func<int, string[]> _hairStyleNames;
        private static Func<int, int, int> _shownHairStyle;
        private static Func<int, bool> _hasMuscle, _hasBodyShape;
        private static Func<int, float> _muscleDefault;
        private static Func<string> _defaultLook;
        private static Func<string, string, float> _getField;
        private static Func<string, string, float, string> _setField;
        private static Action<GameObject, string, float> _applyLook;
        private static Action<GameObject> _treatAsCreature;
        private static Type _visualType;
        private static Func<int, string[]> _tattooDesignNames;
        private static Func<string[]> _tattooAreas, _tattooInkNames;
        private static Func<string, int, int> _getTattooDesign, _getTattooInk;
        private static Func<string, int, int, int, string> _setTattoo;
        private static Action<GameObject, string> _applyTattoos;
        private static Func<int, bool> _hasBeards;
        private static Func<string[]> _beardNames;
        private static Action<GameObject, int> _applyBeard;

        public static bool Available { get { Find(); return _applyLook != null; } }

        private static void Find()
        {
            if (_looked) return;
            _looked = true;
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(Guid, out BepInEx.PluginInfo plugin) || plugin.Instance == null) return;
                Assembly assembly = plugin.Instance.GetType().Assembly;
                Type api = assembly.GetType("BodyReplacer.EinherjarLookApi");
                FieldInfo version = api?.GetField("Version", BindingFlags.Public | BindingFlags.Static);
                if (version == null || (int)version.GetValue(null) != ApiVersion)
                {
                    VillagePlebsPlugin.Instance?.Note("BodyReplacer is installed without a look API this version understands: companions keep its default look");
                    return;
                }
                _replaces = Bind<Func<int, bool, bool>>(api, "Replaces");
                _hairStyleIds = Bind<Func<int, int[]>>(api, "HairStyleIds");
                _hairStyleNames = Bind<Func<int, string[]>>(api, "HairStyleNames");
                _shownHairStyle = Bind<Func<int, int, int>>(api, "ShownHairStyle");
                _hasMuscle = Bind<Func<int, bool>>(api, "HasMuscle");
                _muscleDefault = Bind<Func<int, float>>(api, "MuscleDefault");
                _hasBodyShape = Bind<Func<int, bool>>(api, "HasBodyShape");
                _defaultLook = Bind<Func<string>>(api, "DefaultLook");
                _getField = Bind<Func<string, string, float>>(api, "GetField");
                _setField = Bind<Func<string, string, float, string>>(api, "SetField");
                _treatAsCreature = Bind<Action<GameObject>>(api, "TreatAsCreature");
                _applyLook = Bind<Action<GameObject, string, float>>(api, "ApplyLook");
                _visualType = assembly.GetType("BodyReplacer.EinherjarVisual");
                FindNewer(api);
                VillagePlebsPlugin.Instance?.Note("BodyReplacer found: companions can be given its hairstyles and colours"
                    + (HasTattoos ? ", tattoos" : "") + (HasBeardApi ? ", beards" : ""));
            }
            catch (Exception e)
            {
                _applyLook = null;
                VillagePlebsPlugin.Instance?.Warn("Could not use BodyReplacer's look API: " + e.Message);
            }
        }

        private static void FindNewer(Type api)
        {
            _tattooAreas = TryBind<Func<string[]>>(api, "TattooAreas");
            _tattooDesignNames = TryBind<Func<int, string[]>>(api, "TattooDesignNames");
            _tattooInkNames = TryBind<Func<string[]>>(api, "TattooInkNames");
            _getTattooDesign = TryBind<Func<string, int, int>>(api, "GetTattooDesign");
            _getTattooInk = TryBind<Func<string, int, int>>(api, "GetTattooInk");
            _setTattoo = TryBind<Func<string, int, int, int, string>>(api, "SetTattoo");
            _applyTattoos = TryBind<Action<GameObject, string>>(api, "ApplyTattoos");
            _hasBeards = TryBind<Func<int, bool>>(api, "HasBeards");
            _beardNames = TryBind<Func<string[]>>(api, "BeardNames");
            _applyBeard = TryBind<Action<GameObject, int>>(api, "ApplyBeard");
        }

        private static T TryBind<T>(Type api, string name) where T : Delegate
        {
            MethodInfo method = api.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            try { return method == null ? null : (T)Delegate.CreateDelegate(typeof(T), method); }
            catch (ArgumentException) { return null; }
        }

        private static T Bind<T>(Type api, string name) where T : Delegate
        {
            MethodInfo method = api.GetMethod(name, BindingFlags.Public | BindingFlags.Static) ?? throw new MissingMethodException(api.FullName, name);
            return (T)Delegate.CreateDelegate(typeof(T), method);
        }

        public static bool Replaces(int model) => Available && _replaces(model, false);

        public static int[] HairStyleIds(int model) => Available ? _hairStyleIds(model) : Array.Empty<int>();
        public static string[] HairStyleNames(int model) => Available ? _hairStyleNames(model) : Array.Empty<string>();
        public static int ShownHairStyle(int model, int style) => Available ? _shownHairStyle(model, style) : style;
        public static bool HasMuscle(int model) => Available && _hasMuscle(model);
        public static float MuscleDefault(int model) => Available ? _muscleDefault(model) : 50f;
        public static bool HasBodyShape(int model) => Available && _hasBodyShape(model);
        public static string DefaultLook() => Available ? _defaultLook() : "";
        public static float Get(string look, string field) => Available ? _getField(look, field) : 0f;
        public static string Set(string look, string field, float value) => Available ? _setField(look, field, value) : look;

        public static void TreatAsCreature(GameObject character) { if (Available) _treatAsCreature(character); }

        public static void Apply(GameObject character, string look, float muscle)
        {
            if (Available && !string.IsNullOrEmpty(look)) _applyLook(character, look, muscle);
        }

        public static bool HasTattoos
        {
            get
            {
                Find();
                return _tattooAreas != null && _tattooDesignNames != null && _tattooInkNames != null && _getTattooDesign != null && _getTattooInk != null && _setTattoo != null && _applyTattoos != null;
            }
        }

        public static string[] TattooAreas() => HasTattoos ? _tattooAreas() : Array.Empty<string>();
        public static string[] TattooDesignNames(int area) => HasTattoos ? _tattooDesignNames(area) : Array.Empty<string>();
        public static string[] TattooInkNames() => HasTattoos ? _tattooInkNames() : Array.Empty<string>();
        public static int TattooDesign(string tattoos, int area) => HasTattoos ? _getTattooDesign(tattoos, area) : 0;
        public static int TattooInk(string tattoos, int area) => HasTattoos ? _getTattooInk(tattoos, area) : 0;
        public static string SetTattoo(string tattoos, int area, int design, int ink) => HasTattoos ? _setTattoo(tattoos, area, design, ink) : tattoos;

        public static void ApplyTattoos(GameObject character, string tattoos) { if (HasTattoos) _applyTattoos(character, tattoos ?? ""); }

        private static bool HasBeardApi { get { Find(); return _hasBeards != null && _beardNames != null && _applyBeard != null; } }

        // Beards drawn by BodyReplacer on its own body for this model (its male): index 0 is none, then the beards.
        public static bool HasBeards(int model) => HasBeardApi && _hasBeards(model);
        public static string[] BeardNames() => HasBeardApi ? _beardNames() : Array.Empty<string>();

        public static void ApplyBeard(GameObject character, int beard) { if (HasBeardApi) _applyBeard(character, beard); }

        public static bool Drawn(GameObject character)
        {
            if (!Available || _visualType == null || character == null) return false;
            return character.GetComponent(_visualType) is Behaviour b && b.enabled;
        }
    }
}
