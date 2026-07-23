using HarmonyLib;
using UnityEngine;
using VanillaTradingExpanded;
using Verse;

namespace InvisibleHand;

//VTE item news becomes a demand shock calibrated to peak near the 1–5% price impact. Demand fades over ~30 days and the resulting stock imbalance recovers at the market's own pace 
//company and stock-market news is untouched.
[HarmonyPatchCategory(CompatibilityBootstrap.OptionalCategory)]
[HarmonyPatch(typeof(NewsWorker_TradeItemsImpact), nameof(NewsWorker_TradeItemsImpact.AffectPrices))]
public static class NewsWorker_TradeItemsImpact_AffectPrices_Patch
{
    public static bool Prefix(News news)
    {
        var st = MarketState.Instance;
        if (!CompatibilityStatus.EngineEnabled || st == null)
        {
            return true;
        }
        if (news == null || float.IsNaN(news.priceImpact) || float.IsInfinity(news.priceImpact) || news.priceImpact == 0f)
        {
            return false;
        }
        //VTE clamps its impact to [0,1] before applying. This mirrors that so a user-configured multiplier cannot throw in larger values
        float impact = Mathf.Clamp(Mathf.Abs(news.priceImpact), 0f, 1f);
        float sign = Mathf.Sign(news.priceImpact);

        foreach (var def in news.AffectedThingDefs())
        {
            if (def == null
                || Classifier.Classify(def) == Archetype.Excluded
                || !st.c0Units.ContainsKey(def))
            {
                continue; //never mint shock records for defs the engine won't simulate
            }
            var p = Classifier.ProfileFor(def);
            float kick = sign * Mathf.Log(1f + impact) / (float)MarketMath.PeakGain(
                p.depthDays, p.alpha, p.demandElasticity, p.supplyElasticity,
                MarketTuning.NewsShockEFoldingDays);
            st.newsShock.TryGetValue(def, out var current);
            st.newsShock[def] = Mathf.Clamp(current + kick,
                -MarketTuning.NewsShockMax, MarketTuning.NewsShockMax);
        }
        return false;
    }
}
