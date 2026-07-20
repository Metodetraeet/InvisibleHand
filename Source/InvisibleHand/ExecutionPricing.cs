using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace InvisibleHand;

//trades execute at the average price across the price range they traverse (implemented in AvgOverSpot). As a result, buying and then immediately selling always loses the spread
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
        //include today's not-yet-applied trades, so consecutive deals in the same day walk the curve cumulatively
        st.pendingUnits.TryGetValue(def, out var pending);
        s = Mathf.Max(s + pending, sStar * MarketTuning.StockFloorFraction);

        float x = count / s;
        float factor;
        if (selling)
        {
            factor = AvgOverSpot(x, profile.alpha, up: true);
        }
        else
        {
            x = Mathf.Min(x, 0.95f); //cannot buy a market to literal zero
            factor = AvgOverSpot(x, profile.alpha, up: false);
        }
        float relSpot = Mathf.Clamp(Mathf.Pow(sStar / s, profile.alpha),
            MarketTuning.PriceRatioMin, MarketTuning.PriceRatioMax);
        return Mathf.Clamp(factor,
            MarketTuning.PriceRatioMin / relSpot,
            MarketTuning.PriceRatioMax / relSpot);
    }

    //path-average execution price relative to spot, p(S) ∝ S^-alpha
    //x = trade size / current stock
    //sell: ((1+x)^(1-a)-1)/((1-a)x)
    //buy: (1-(1-x)^(1-a))/((1-a)x)
    //at alpha = 1, use the logarithmic limit forms
    private static float AvgOverSpot(float x, float alpha, bool up)
    {
        if (x < 1e-6f)
        {
            return 1f;
        }
        if (Mathf.Abs(alpha - 1f) < 1e-4f)
        {
            return up ? Mathf.Log(1f + x) / x : -Mathf.Log(1f - x) / x;
        }
        float oneMinusA = 1f - alpha;
        return up
            ? (Mathf.Pow(1f + x, oneMinusA) - 1f) / (oneMinusA * x)
            : (1f - Mathf.Pow(1f - x, oneMinusA)) / (oneMinusA * x);
    }
}
