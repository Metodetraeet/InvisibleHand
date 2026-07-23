using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace InvisibleHand;

//trades execute at the average price across the price range they traverse (implemented in MarketMath.AverageClampedCurve). As a result, buying and then immediately selling always loses the spread
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

        //the incoming __result is anchored at yesterday's closing stock
        return (float)MarketMath.ExecutionImpactFactor(
            sStar, s, s + pending, count, selling, profile.alpha,
            MarketTuning.StockFloorFraction,
            MarketTuning.PriceRatioMin, MarketTuning.PriceRatioMax);
    }
}