using System;
using Verse;

namespace InvisibleHand;

//game-facing entry point for market randomness. Gathers world identity from Verse,
//delegates all deterministic work to MarketRandomCore.
public static class MarketRandom
{
    private const string PackageId = "metodetraeet.InvisibleHand";

    //world identity cache. Recomputed automatically when seedString or
    //persistentRandomValue changes, so a new game in the same process
    //can never inherit the previous world's random path.
    private static string cachedSeedString;
    private static int cachedPersistentValue;
    private static ulong cachedWorldHash;
    private static bool hasCachedWorldHash;

    public static double Gaussian(ThingDef def, int day,
        MarketRandomStream stream, int drawIndex = 0)
    {
        //defName is the stable definition identity - never shortHash alone,
        //never iteration order
        return Gaussian(def?.defName ?? "(null)", day, stream, drawIndex);
    }

    //archetype-wide draws use the stable enum name rather than a ThingDef
    public static double Gaussian(Archetype archetype, int day,
        MarketRandomStream stream, int drawIndex = 0)
    {
        return Gaussian("archetype:" + archetype, day, stream, drawIndex);
    }

    public static double Gaussian(string identity, int day,
        MarketRandomStream stream, int drawIndex = 0)
    {
        ulong key = MarketRandomCore.DrawKey(
            WorldBaseHash(), identity, day, (int)stream, drawIndex);
        return MarketRandomCore.GaussianFromKey(key);
    }

    private static ulong WorldBaseHash()
    {
        var info = Current.Game?.World?.info;
        string seed = info?.seedString ?? string.Empty;
        int persistent = info?.persistentRandomValue ?? 0;
        if (!hasCachedWorldHash
            || persistent != cachedPersistentValue
            || !string.Equals(seed, cachedSeedString, StringComparison.Ordinal))
        {
            cachedWorldHash = MarketRandomCore.WorldBaseHash(
                PackageId, MarketRandomCore.MarketRngVersion, seed, persistent);
            cachedSeedString = seed;
            cachedPersistentValue = persistent;
            hasCachedWorldHash = true;
        }
        return cachedWorldHash;
    }
}
