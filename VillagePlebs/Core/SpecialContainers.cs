namespace VillagePlebs.Shared
{
    /// <summary>
    /// Built pieces that have a chest's Container but are not for keeping things, which a companion's own chests must leave alone: the
    /// game's Obliterator (an Incinerator in the piece), where what is in it burns when the lever is pulled.
    /// </summary>
    internal static class SpecialContainers
    {
        public static bool NotStorage(Container c)
        {
            if (c == null) return false;
            Piece piece = c.GetComponentInParent<Piece>();
            return (piece != null ? piece.GetComponentInChildren<Incinerator>(true) : c.GetComponentInParent<Incinerator>()) != null;
        }
    }
}
