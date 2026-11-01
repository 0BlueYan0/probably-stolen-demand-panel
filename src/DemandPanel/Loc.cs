namespace DemandPanel
{
    internal static class Loc
    {
        public static string T(string key, params object[] args)
        {
            return ModHelper.GetLocalized(ModEntry.ModId, key, args);
        }
    }
}
