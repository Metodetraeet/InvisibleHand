using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace InvisibleHand;

//trades execute at the average price across the price range they traverse (implemented in AvgClampedCurve). As a result, buying and then immediately selling always loses the spread
//effective stock includes same-day pending trades so splitting a dump into many deals can't recover flat pricing
[HarmonyPatch(typeof(Tradeable), nameof(Tradeable.GetPriceFor))]
public static class Tradeable_GetPriceFor_Patch
{
    public static void Postfix(Tradeable __instance, TradeAction action, ref float __result)
    {
        int count = Mathf.Abs(__instance.CountToTransfer);
        if (count == 0 || __instance.IsCurrency || __instance.ThingDef == null)
        {
            return;
        }
        __result *= ExecutionPricing.ImpactFactor(
            __instance.ThingDef, count, action == TradeAction.PlayerSells);
    }
}

public static class ExecutionPricing
{
    public static float ImpactFactor(ThingDef def, int count, bool selling)
    {
        var st = MarketState.Instance;
        if (st == null || count <= 0
            || !st.c0Units.TryGetValue(def, out var c0) || c0 <= 0f
            || Classifier.Classify(def) == Archetype.Excluded)
        {
            return 1f;
        }
        var profile = Classifier.ProfileFor(def);
        float sStar = profile.depthDays * c0;
        if (sStar <= 0f)
        {
            return 1f;
        }
        st.stock.TryGetValue(def, out var s);
        if (s <= 0f)
        {
            s = sStar;
        }
        float sClose = Mathf.Max(s, sStar * MarketTuning.StockFloorFraction); //this is what VTE's price dict (and __result) is anchored to until our later tick
        //include today's not-yet-applied trades, so consecutive deals in the same day walk the curve cumulatively
        st.pendingUnits.TryGetValue(def, out var pending);
        s = Mathf.Max(s + pending, sStar * MarketTuning.StockFloorFraction);

        float avgAbs;
        if (selling)
        {
            avgAbs = AvgClampedCurve(sStar, s, s + count, profile.alpha);
        }
        else
        {
            //a purchase can exceed the market's drainable stock. The curve prices only down to the same 2% floor
            //this prevents undercharging big buys and should preserve split invariance
            float physicalFloor = sStar * MarketTuning.StockFloorFraction;
            float curveUnits = Mathf.Min(count, Mathf.Max(s - physicalFloor, 0f));
            float excessUnits = count - curveUnits;
            float total = 0f;
            if (curveUnits > 0f)
            {
                total += curveUnits * AvgClampedCurve(sStar, s - curveUnits, s, profile.alpha);
            }
            if (excessUnits > 0f)
            {
                float floorPrice = Mathf.Clamp(
                    Mathf.Pow(sStar / physicalFloor, profile.alpha),
                    MarketTuning.PriceRatioMin, MarketTuning.PriceRatioMax);
                total += excessUnits * floorPrice;
            }
            avgAbs = total / count;
        }

        //__result is priced at yesterday's closing stock, while avgAbs is measured on today's effective stock. Without this division, splitting a dump into many deals would decrease the price
        float relClose = Mathf.Clamp(Mathf.Pow(sStar / sClose, profile.alpha),
            MarketTuning.PriceRatioMin, MarketTuning.PriceRatioMax);
        return Mathf.Clamp(avgAbs, MarketTuning.PriceRatioMin, MarketTuning.PriceRatioMax) / relClose;
    }

    //closed-form average of the clamped marginal price ratio
    //marginal price at stock S: (S*/S)^alpha, clamped to [PriceRatioMin, PriceRatioMax]
    //sell: I(v) = (1 - (1+v)^(1-a))/(a-1)   (a != 1),   ln(1+v)  (a = 1)
    //buy:  I(v) = ((1-v)^(1-a) - 1)/(a-1)   (a != 1),  -ln(1-v)  (a = 1)
    //average of clamp((S*/S')^a, PriceRatioMin, PriceRatioMax) over S' in [lo, hi]
    private static float AvgClampedCurve(float sStarF, float loF, float hiF, float alphaF)
    {
        double sStar = sStarF, lo = loF, hi = hiF, a = alphaF;
        if (hi - lo < 1e-9)
        {
            double rel = System.Math.Pow(sStar / lo, a);
            return (float)System.Math.Min(System.Math.Max(rel, MarketTuning.PriceRatioMin), MarketTuning.PriceRatioMax);
        }
        // Stock thresholds where the raw curve crosses the band edges.
        double ceilingStock = sStar * System.Math.Pow(MarketTuning.PriceRatioMax, -1.0 / a);
        double floorStock = sStar * System.Math.Pow(MarketTuning.PriceRatioMin, -1.0 / a);
        double total = 0.0;
        // Scarcity plateau: everything below ceilingStock trades at the ceiling.
        double c2 = System.Math.Min(hi, ceilingStock);
        if (c2 > lo)
        {
            total += (c2 - lo) * MarketTuning.PriceRatioMax;
        }
        // Power curve in the middle.
        double m1 = System.Math.Max(lo, ceilingStock);
        double m2 = System.Math.Min(hi, floorStock);
        if (m2 > m1)
        {
            total += System.Math.Abs(a - 1.0) < 1e-9
                ? sStar * System.Math.Log(m2 / m1)
                : System.Math.Pow(sStar, a) * (System.Math.Pow(m2, 1.0 - a) - System.Math.Pow(m1, 1.0 - a)) / (1.0 - a);
        }
        // Glut plateau: everything above floorStock trades at the floor.
        double f1 = System.Math.Max(lo, floorStock);
        if (hi > f1)
        {
            total += (hi - f1) * MarketTuning.PriceRatioMin;
        }
        return (float)(total / (hi - lo));
    }
}