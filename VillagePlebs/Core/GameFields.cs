using System.Collections.Generic;
using HarmonyLib;

namespace VillagePlebs
{
    /// <summary>The game's private fields the mod reads in several places, looked up once here (each place used to look up its own).</summary>
    internal static class GameFields
    {
        /// <summary>A character's animation sync (its poses: sitting, in bed, on a seat).</summary>
        public static readonly AccessTools.FieldRef<Character, ZSyncAnimation> Anim = AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");

        /// <summary>Every item lying in the world.</summary>
        public static readonly AccessTools.FieldRef<List<ItemDrop>> ItemDrops = AccessTools.StaticFieldRefAccess<List<ItemDrop>>(AccessTools.Field(typeof(ItemDrop), "s_instances"));

        /// <summary>Every crafting station in the world.</summary>
        public static readonly AccessTools.FieldRef<List<CraftingStation>> Stations = AccessTools.StaticFieldRefAccess<List<CraftingStation>>(AccessTools.Field(typeof(CraftingStation), "m_allStations"));

        /// <summary>The map's pins.</summary>
        public static readonly AccessTools.FieldRef<Minimap, List<Minimap.PinData>> Pins = AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");
    }
}
