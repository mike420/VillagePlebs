namespace VillagePlebs.Shared
{
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
