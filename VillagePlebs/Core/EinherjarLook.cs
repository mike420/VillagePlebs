using System;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace VillagePlebs
{
    /// <summary>
    /// Einherjar Bodies' look API, found by name when that mod is installed: a companion's eyes, underwear and tattoos on its bodies.
    /// A look is the eyes as text; its fields (EyeHue, EyeSaturation, EyeValue, Glow, IrisSize, HideIris) are each from 0 to 1.
    /// </summary>
    internal static class EinherjarLook
    {
        private const string Guid = "MagicMike.EinherjarBodies";
        private const int ApiVersion = 1;

        private static bool _looked;
        private static Func<int, bool, bool> _replaces;
        private static Func<GameObject, bool> _drawn;
        private static Func<string> _defaultLook;
        private static Func<string, string, float> _getField;
        private static Func<string, string, float, string> _setField;
        private static Action<GameObject, string> _applyLook;
        private static Func<string[]> _tattooAreas, _tattooInkNames;
        private static Func<int, string[]> _tattooDesignNames;
        private static Func<string, int, int> _getTattooDesign, _getTattooInk;
        private static Func<string, int, int, int, string> _setTattoo;
        private static Action<GameObject, string> _applyTattoos;
        private static Func<string, int, float> _underwearPart;
        private static Func<string, int, float, string> _setUnderwearPart;
        private static Action<GameObject, string> _applyUnderwear;

        public static bool Available { get { Find(); return _applyLook != null; } }

        private static void Find()
        {
            if (_looked) return;
            _looked = true;
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(Guid, out BepInEx.PluginInfo plugin) || plugin.Instance == null) return;
                Type api = plugin.Instance.GetType().Assembly.GetType("EinherjarBodies.EinherjarLookApi");
                FieldInfo version = api?.GetField("Version", BindingFlags.Public | BindingFlags.Static);
                if (version == null || (int)version.GetValue(null) != ApiVersion)
                {
                    VillagePlebsPlugin.Instance?.Note("Einherjar Bodies is installed without a look API this version understands: companions keep their default look");
                    return;
                }
                _replaces = Bind<Func<int, bool, bool>>(api, "Replaces");
                _drawn = Bind<Func<GameObject, bool>>(api, "Drawn");
                _defaultLook = Bind<Func<string>>(api, "DefaultLook");
                _getField = Bind<Func<string, string, float>>(api, "GetField");
                _setField = Bind<Func<string, string, float, string>>(api, "SetField");
                _applyLook = Bind<Action<GameObject, string>>(api, "ApplyLook");
                _tattooAreas = Bind<Func<string[]>>(api, "TattooAreas");
                _tattooDesignNames = Bind<Func<int, string[]>>(api, "TattooDesignNames");
                _tattooInkNames = Bind<Func<string[]>>(api, "TattooInkNames");
                _getTattooDesign = Bind<Func<string, int, int>>(api, "GetTattooDesign");
                _getTattooInk = Bind<Func<string, int, int>>(api, "GetTattooInk");
                _setTattoo = Bind<Func<string, int, int, int, string>>(api, "SetTattoo");
                _applyTattoos = Bind<Action<GameObject, string>>(api, "ApplyTattoos");
                _underwearPart = Bind<Func<string, int, float>>(api, "UnderwearPart");
                _setUnderwearPart = Bind<Func<string, int, float, string>>(api, "SetUnderwearPart");
                _applyUnderwear = Bind<Action<GameObject, string>>(api, "ApplyUnderwear");
                VillagePlebsPlugin.Instance?.Note("Einherjar Bodies found: companions can be given its eye colours, underwear colours and tattoos");
            }
            catch (Exception e)
            {
                _applyLook = null;
                VillagePlebsPlugin.Instance?.Warn("Could not use Einherjar Bodies' look API: " + e.Message);
            }
        }

        private static T Bind<T>(Type api, string name) where T : Delegate
        {
            MethodInfo method = api.GetMethod(name, BindingFlags.Public | BindingFlags.Static) ?? throw new MissingMethodException(api.FullName, name);
            return (T)Delegate.CreateDelegate(typeof(T), method);
        }

        // Whether this model is drawn with an Einherjar body for a companion
        public static bool Replaces(int model) => Available && _replaces(model, false);

        public static string DefaultLook() => Available ? _defaultLook() : "";
        public static float Get(string look, string field) => Available ? _getField(look, field) : 0f;
        public static string Set(string look, string field, float value) => Available ? _setField(look, field, value) : look;

        public static void Apply(GameObject character, string look)
        {
            if (Available && !string.IsNullOrEmpty(look)) _applyLook(character, look);
        }

        public static string[] TattooAreas() => Available ? _tattooAreas() : Array.Empty<string>();
        public static string[] TattooDesignNames(int area) => Available ? _tattooDesignNames(area) : Array.Empty<string>();
        public static string[] TattooInkNames() => Available ? _tattooInkNames() : Array.Empty<string>();
        public static int TattooDesign(string tattoos, int area) => Available ? _getTattooDesign(tattoos, area) : 0;
        public static int TattooInk(string tattoos, int area) => Available ? _getTattooInk(tattoos, area) : 0;
        public static string SetTattoo(string tattoos, int area, int design, int ink) => Available ? _setTattoo(tattoos, area, design, ink) : tattoos;

        public static void ApplyTattoos(GameObject character, string tattoos) { if (Available) _applyTattoos(character, tattoos ?? ""); }

        // Underwear colour: its hue (0), saturation (1) or brightness (2), each from 0 to 1
        public static float UnderwearPart(string colour, int part) => Available ? _underwearPart(colour, part) : 0f;
        public static string SetUnderwearPart(string colour, int part, float value) => Available ? _setUnderwearPart(colour, part, value) : colour;

        public static void ApplyUnderwear(GameObject character, string colour) { if (Available) _applyUnderwear(character, colour ?? ""); }

        public static bool Drawn(GameObject character) => Available && character != null && _drawn(character);
    }
}
