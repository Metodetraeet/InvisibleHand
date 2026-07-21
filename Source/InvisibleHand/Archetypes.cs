using System.Collections.Generic;
using Verse;
using UnityEngine;

namespace InvisibleHand;

public enum Archetype
{
    Unset, //never assigned by classifier
    Excluded,
    Food,
    LuxuryConsumable,
    Medical,
    RawMaterial,
    PreciousDurable,
    Manufactured,
    Collectible,
    Livestock,
    Slaves,
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
        { Archetype.Livestock,        new MarketProfile(35f,  0.9f, 0.6f, 0.6f, 2.0f) },
        { Archetype.Slaves,           new MarketProfile(25f, 1.2f, 0.5f, 0.3f, 2.0f) },
        { Archetype.General,          new MarketProfile(45f,  1.0f, 0.6f, 0.6f, 2.5f)}
    };

    public static readonly Dictionary<Archetype, float> BudgetShareByArchetype = new() //fraction of total market spend. sums to 1.0 across all archetypes. Used to estimate market size for each archetype.
    {
        { Archetype.Food, 0.25f },
        { Archetype.RawMaterial, 0.22f },
        { Archetype.Manufactured, 0.14f },
        { Archetype.LuxuryConsumable, 0.11f },
        { Archetype.Livestock, 0.08f },
        { Archetype.Medical, 0.06f },
        { Archetype.PreciousDurable, 0.05f },
        { Archetype.General, 0.04f },
        { Archetype.Slaves, 0.03f },
        { Archetype.Collectible, 0.02f }
    };
}

public static class MarketTuning
{
    public const float ReferenceWorldFlow = 50_000f; //ReferenceWorldFlow is the one absolute number in the model. The other numbers are clamps
    public const float ActivityRatioMin = 0.25f;
    public const float ActivityRatioMax = 2.0f;
    public const float InitPriceRatioMin = 0.2f;
    public const float InitPriceRatioMax = 5.0f;
    public const float PriceRatioMin = 0.1f;   //output clamp
    public const float PriceRatioMax = 10.0f;  //scarcity ceiling
    public const float StockFloorFraction = 0.02f; //floor stock at 2% of equilibrium
    public const float FlowNoiseSigma = 0.03f; //stddev of the daily log-normal flow noise

    //ambient demand shocks: AR(1) log-multiplier per def, 15-day memory
    public const float ShockMemoryDays = 15f;
    public const float ShockSigma = 0.014f; //sigma 0.014 measured in sim. Stationary price vol lands 1.-3.0% across archetypes
    public static readonly float ShockRho = Mathf.Exp(-1f / ShockMemoryDays);
    public static readonly float ShockMeanCorrection =
        0.5f * ShockSigma * ShockSigma / (1f - ShockRho * ShockRho); //mean-one correction (same idea as FlowNoise)
}

public class MarketProfileExtension : DefModExtension
{
    public Archetype archetype = Archetype.Unset;
    public float depthDays = -1f;
    public float alpha = -1f;
    public float demandElasticity = -1f;
    public float supplyElasticity = -1f;
    public float drainCap = -1f;
}

