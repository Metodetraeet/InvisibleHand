using HarmonyLib;
using UnityEngine;
using VanillaTradingExpanded;
using Verse;

namespace InvisibleHand;

//VTE item news becomes a demand shock calibrated to peak near the 1–5% price impact. Demand fades over ~30 days and the resulting stock imbalance recovers at the market's own pace 
//company and stock-market news is untouched.
[HarmonyPatch(typeof(NewsWorker_TradeItemsImpact), nameof(NewsWorker_TradeItemsImpact.AffectPrices))]
public static class NewsWorker_TradeItemsImpact_AffectPrices_Patch
{
    public static bool Prefix(News news)
    {
        var st = MarketState.Instance;
        if (st == null)
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
            float kick = sign * Mathf.Log(1f + impact) / PeakGain(p);
            st.newsShock.TryGetValue(def, out var current);
            st.newsShock[def] = Mathf.Clamp(current + kick,
                -MarketTuning.NewsShockMax, MarketTuning.NewsShockMax);
        }
        return false;
    }

    //linearized price response used to derive peak gain:
    //pi(t) = K*a*(e^-lambda*t - e^-beta*t)/(beta-lambda):
    //a = alpha/depth, lambda = alpha*(ed+es)/depth, beta = 1/eFold.
    private static float PeakGain(MarketProfile p)
    {
        float a = p.alpha / p.depthDays;
        float lambda = p.alpha * (p.demandElasticity + p.supplyElasticity) / p.depthDays;
        float beta = 1f / MarketTuning.NewsShockEFoldingDays;
        if (Mathf.Abs(beta - lambda) < 1e-5f)
        {
            float ts = 1f / beta;
            return Mathf.Max(a * ts * Mathf.Exp(-beta * ts), 1e-4f);
        }
        float tStar = Mathf.Log(beta / lambda) / (beta - lambda);
        float g = a * (Mathf.Exp(-lambda * tStar) - Mathf.Exp(-beta * tStar)) / (beta - lambda);
        return Mathf.Max(g, 1e-4f);
    }
}
