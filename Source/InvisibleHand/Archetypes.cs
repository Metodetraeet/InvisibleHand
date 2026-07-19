using System.Collections.Generic;
using Verse;

namespace InvisibleHand;

public enum Archetype
{
    Food,
    LuxuryConsumable,
    Medical,
    RawMaterial,
    PreciousDurable,
    Manufactured,
    Collectible,
    General
}



public readonly struct MarketProfile
{
    public readonly float depthDays; //market depth in days of baseline volume. Higher = trades move price less, and displacements persists longer
    public readonly float alpha; //price-curve exponebnt (doubling stock cuts price by (1/2)^α)
    public readonly float demandElasticity; //how hard demand responds to price changes
    public readonly float supplyElasticity; //how hard supply responds to price changes
    public readonly float drainCap; //ceiling on the demand multiplier. A crashed price can boost consumption at most cap× baseline (prevents crashed markets from absorbing gluts unrealistically fast)

    public MarketProfile(float depthDays, float alpha, float demandElasticity,
        float supplyElasticity, float drainCap)
    {
        this.depthDays = depthDays;
        this.alpha = alpha;
        this.demandElasticity = demandElasticity;
        this.supplyElasticity = supplyElasticity;
        this.drainCap = drainCap;
    }
}

public static class MarketProfiles //tune later!
{
    public static readonly Dictionary<Archetype, MarketProfile> ByArchetype = new()
    {
        { Archetype.Food,             new MarketProfile(30f,  0.6f, 0.4f, 0.8f, 3.0f) },
        { Archetype.LuxuryConsumable, new MarketProfile(30f,  1.0f, 0.8f, 0.5f, 2.5f) },
        { Archetype.Medical,          new MarketProfile(40f,  0.9f, 0.3f, 0.6f, 2.0f) },
        { Archetype.RawMaterial,      new MarketProfile(60f,  0.8f, 0.6f, 0.7f, 2.5f) },
        { Archetype.PreciousDurable,  new MarketProfile(120f, 1.5f, 0.3f, 0.6f, 2.0f) },
        { Archetype.Manufactured,     new MarketProfile(45f,  1.0f, 0.7f, 0.5f, 2.5f) },
        { Archetype.Collectible,      new MarketProfile(90f,  1.3f, 0.5f, 0.3f, 2.0f) },
        { Archetype.General,          new MarketProfile(45f,  1.0f, 0.6f, 0.6f, 2.5f)}
    };

    public static readonly Dictionary<Archetype, float> BudgetShareByArchetype = new() //fraction of total market spend. sums to 1.0 across all archetypes. Used to estimate market size for each archetype.
{
    { Archetype.Food, 0.30f },
    { Archetype.RawMaterial, 0.25f },
    { Archetype.Manufactured, 0.15f },
    { Archetype.LuxuryConsumable, 0.12f },
    { Archetype.Medical, 0.06f },
    { Archetype.PreciousDurable, 0.05f },
    { Archetype.General, 0.05f },
    { Archetype.Collectible, 0.02f }
};
}

public class MarketProfileExtension : DefModExtension
{
    public float depthDays = 45f;
    public float alpha = 1.0f;
    public float demandElasticity = 0.6f;
    public float supplyElasticity = 0.6f;
    public float drainCap = 2.5f;
}
