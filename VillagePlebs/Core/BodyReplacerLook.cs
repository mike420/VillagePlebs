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
        // (a newer BodyReplacer's, found by name: the game's hairstyles fitted to its bodies, and tattoos; null with an older one)
        private static Func<int, string[]> _gameHairs, _gameHairNames, _tattooDesignNames;
        private static int _gameHairStyle = -1;
        private static Func<string[]> _tattooAreas, _tattooInkNames;
        private static Func<string, int, int> _getTattooDesign, _getTattooInk;
        private static Func<string, int, int, int, string> _setTattoo;
        private static Action<GameObject, string> _applyTattoos;

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
                    + (HasGameHair ? ", the game's hairstyles" : "") + (HasTattoos ? " and tattoos" : ""));
            }
            catch (Exception e)
            {
                _applyLook = null;
                VillagePlebsPlugin.Instance?.Warn("Could not use BodyReplacer's look API: " + e.Message);
            }
        }

        // What a newer BodyReplacer adds (an older one lacks them: those parts of the Looks tab don't show)
        private static void FindNewer(Type api)
        {
            _gameHairs = TryBind<Func<int, string[]>>(api, "GameHairs");
            _gameHairNames = TryBind<Func<int, string[]>>(api, "GameHairNames");
            object style = api.GetField("GameHairStyle", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            _gameHairStyle = style is int s ? s : -1;
            _tattooAreas = TryBind<Func<string[]>>(api, "TattooAreas");
            _tattooDesignNames = TryBind<Func<int, string[]>>(api, "TattooDesignNames");
            _tattooInkNames = TryBind<Func<string[]>>(api, "TattooInkNames");
            _getTattooDesign = TryBind<Func<string, int, int>>(api, "GetTattooDesign");
            _getTattooInk = TryBind<Func<string, int, int>>(api, "GetTattooInk");
            _setTattoo = TryBind<Func<string, int, int, int, string>>(api, "SetTattoo");
            _applyTattoos = TryBind<Action<GameObject, string>>(api, "ApplyTattoos");
        }

        private static T TryBind<T>(Type api, string name) where T : Delegate
        {
            MethodInfo method = api.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            try { return method == null ? null : (T)Delegate.CreateDelegate(typeof(T), method); }
            catch (ArgumentException) { return null; }   // (there, but not as this expects)
        }

        private static T Bind<T>(Type api, string name) where T : Delegate
        {
            MethodInfo method = api.GetMethod(name, BindingFlags.Public | BindingFlags.Static) ?? throw new MissingMethodException(api.FullName, name);
            return (T)Delegate.CreateDelegate(typeof(T), method);
        }

        /// <summary>Whether BodyReplacer draws its body on a companion with this model (0 male, 1 female).</summary>
        public static bool Replaces(int model) => Available && _replaces(model, false);

        public static int[] HairStyleIds(int model) => Available ? _hairStyleIds(model) : Array.Empty<int>();
        public static string[] HairStyleNames(int model) => Available ? _hairStyleNames(model) : Array.Empty<string>();
        /// <summary>The hairstyle that body shows for a stored one (one it doesn't have shows its default).</summary>
        public static int ShownHairStyle(int model, int style) => Available ? _shownHairStyle(model, style) : style;
        public static bool HasMuscle(int model) => Available && _hasMuscle(model);
        public static float MuscleDefault(int model) => Available ? _muscleDefault(model) : 50f;
        /// <summary>Bust and hip size, on a body that has them.</summary>
        public static bool HasBodyShape(int model) => Available && _hasBodyShape(model);
        public static string DefaultLook() => Available ? _defaultLook() : "";
        public static float Get(string look, string field) => Available ? _getField(look, field) : 0f;
        public static string Set(string look, string field, float value) => Available ? _setField(look, field, value) : look;

        /// <summary>The Looks tab's preview is built from the Player prefab: drawn like a companion, not like a player.</summary>
        public static void TreatAsCreature(GameObject character) { if (Available) _treatAsCreature(character); }

        /// <summary>Gives a character its look (muscle below 0: the body's default). On a companion only its owner can.</summary>
        public static void Apply(GameObject character, string look, float muscle)
        {
            if (Available && !string.IsNullOrEmpty(look)) _applyLook(character, look, muscle);
        }

        // ---- the game's hairstyles on its body, and tattoos (a newer BodyReplacer) ---------------------------------

        /// <summary>Its bodies can wear the game's hairstyles: its look's HairStyle <see cref="GameHairStyle"/> shows the hair item, fitted.</summary>
        public static bool HasGameHair { get { Find(); return _gameHairs != null && _gameHairNames != null && _gameHairStyle >= 0; } }
        public static int GameHairStyle => _gameHairStyle;
        /// <summary>The game's hairstyles that body is fitted for: their item prefab names, and their names to show.</summary>
        public static string[] GameHairs(int model) => HasGameHair ? _gameHairs(model) : Array.Empty<string>();
        public static string[] GameHairNames(int model) => HasGameHair ? _gameHairNames(model) : Array.Empty<string>();

        public static bool HasTattoos
        {
            get
            {
                Find();
                return _tattooAreas != null && _tattooDesignNames != null && _tattooInkNames != null && _getTattooDesign != null && _getTattooInk != null && _setTattoo != null && _applyTattoos != null;
            }
        }

        /// <summary>The tattoo areas' names (face, chest...); an area's designs' names, for designs 1 on (0 is none); the inks' names.</summary>
        public static string[] TattooAreas() => HasTattoos ? _tattooAreas() : Array.Empty<string>();
        public static string[] TattooDesignNames(int area) => HasTattoos ? _tattooDesignNames(area) : Array.Empty<string>();
        public static string[] TattooInkNames() => HasTattoos ? _tattooInkNames() : Array.Empty<string>();
        public static int TattooDesign(string tattoos, int area) => HasTattoos ? _getTattooDesign(tattoos, area) : 0;
        public static int TattooInk(string tattoos, int area) => HasTattoos ? _getTattooInk(tattoos, area) : 0;
        /// <summary>The tattoos (BodyReplacer's text: empty for none) with one area's design and ink changed.</summary>
        public static string SetTattoo(string tattoos, int area, int design, int ink) => HasTattoos ? _setTattoo(tattoos, area, design, ink) : tattoos;

        /// <summary>Gives a character its tattoos (empty: none). On a companion only its owner can.</summary>
        public static void ApplyTattoos(GameObject character, string tattoos) { if (HasTattoos) _applyTattoos(character, tattoos ?? ""); }

        /// <summary>BodyReplacer's body is on it now (it is put on when the game updates the model's equipment, so asked each frame).</summary>
        public static bool Drawn(GameObject character)
        {
            if (!Available || _visualType == null || character == null) return false;
            return character.GetComponent(_visualType) is Behaviour b && b.enabled;
        }
    }
}
