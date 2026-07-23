using System;

namespace InvisibleHand;

public enum MarketRandomStream
{
    AmbientDemandShock = 10,
    MarketInflowNoise = 20,
    MarketOutflowNoise = 30,
    ArchetypeDemandShock = 40
}

//never touches Verse.Rand 
public static class MarketRandomCore
{
    public const int MarketRngVersion = 1;

    private const ulong FnvOffsetBasis = 14695981039346656037UL; //0xCBF29CE484222325
    private const ulong FnvPrime = 1099511628211UL;              //0x100000001B3

    public static ulong Fnv1aStart() => FnvOffsetBasis;

    public static ulong Fnv1a(ulong hash, byte value)
    {
        unchecked
        {
            return (hash ^ value) * FnvPrime;
        }
    }

    public static ulong Fnv1a(ulong hash, int value)
    {
        unchecked
        {
            uint v = (uint)value;
            hash = Fnv1a(hash, (byte)v);
            hash = Fnv1a(hash, (byte)(v >> 8));
            hash = Fnv1a(hash, (byte)(v >> 16));
            hash = Fnv1a(hash, (byte)(v >> 24));
            return hash;
        }
    }

    public static ulong Fnv1a(ulong hash, string text)
    {
        if (text == null)
        {
            return Fnv1a(hash, -1);
        }
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            hash = Fnv1a(hash, (byte)(c & 0xFF));
            hash = Fnv1a(hash, (byte)(c >> 8));
        }
        //length terminator so consecutive string folds can't alias across boundaries
        return Fnv1a(hash, text.Length);
    }

    //recomputed whenever the world changes, so no state leaks between games.
    public static ulong WorldBaseHash(string packageId, int rngVersion,
        string worldSeedString, int worldPersistentValue)
    {
        ulong h = Fnv1aStart();
        h = Fnv1a(h, packageId);
        h = Fnv1a(h, rngVersion);
        h = Fnv1a(h, worldSeedString);
        h = Fnv1a(h, worldPersistentValue);
        return h;
    }

    public static ulong DrawKey(ulong worldBaseHash, string identity,
        int day, int stream, int drawIndex)
    {
        ulong h = worldBaseHash;
        h = Fnv1a(h, identity);
        h = Fnv1a(h, day);
        h = Fnv1a(h, stream);
        h = Fnv1a(h, drawIndex);
        return h;
    }

    public static ulong SplitMix64(ref ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    public static double NextUniform(ref ulong state)
    {
        return ((SplitMix64(ref state) >> 11) + 0.5) * (1.0 / 9007199254740992.0);
    }

    //standard normal from two deterministic uniforms (Box-Muller)
    public static double GaussianFromKey(ulong key)
    {
        ulong state = key;
        double u1 = NextUniform(ref state);
        double u2 = NextUniform(ref state);
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
