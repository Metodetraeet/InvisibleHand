using UnityEngine;
using VanillaTradingExpanded;
using Verse;

namespace InvisibleHand;

//the daily market simulation. Pushes resulting prices into VTE's priceModifiers
public static class MarketEngine
{
    public static void Tick(MarketState st)
    {
        var manager = TradingManager.Instance;
        if (manager == null)
        {
            return;
        }
        manager.priceModifiers ??= new System.Collections.Generic.Dictionary<ThingDef, float>();
        Telemetry.BeginDay(st); //flagged for later removal

        foreach (var def in st.universe)
        {
            if (!st.c0Units.TryGetValue(def, out var c0) || c0 <= 0f)
            {
                continue;
            }
            var profile = Classifier.ProfileFor(def);
            float sStar = profile.depthDays * c0;
            float p0 = Classifier.VanillaMarketValue(def);
            if (sStar <= 0f || p0 <= 0f)
            {
                continue;
            }
            st.stock.TryGetValue(def, out var s);
            if (s <= 0f)
            {
                s = sStar; //unstocked def trades at equilibrium
            }

            //price formed from yesterday's closing stock, flows respond to it today.
            float rel = PriceRatio(sStar, s, profile.alpha);

            //ambient demand shock. Persistent good-months and bad-months. News events also use this - decay is shared.
            st.demandShock.TryGetValue(def, out var shock);
            shock = MarketTuning.ShockRho * shock
                + MarketTuning.ShockSigma * Rand.Gaussian();
            st.demandShock[def] = shock;
            float demandMult = Mathf.Exp(shock - MarketTuning.ShockMeanCorrection);

            //log-normal flow noise, mean exactly 1, so noise adds texture without adding drift
            float consumption = c0
                * Mathf.Min(Mathf.Pow(rel, -profile.demandElasticity), profile.drainCap)
                * demandMult
                * FlowNoise();
            float production = c0
                * Mathf.Pow(rel, profile.supplyElasticity)
                * FlowNoise();

            st.pendingUnits.TryGetValue(def, out var playerNet);

            s = Mathf.Max(s + production - consumption + playerNet,
                sStar * MarketTuning.StockFloorFraction);
            st.stock[def] = s;

            float closing = p0 * PriceRatio(sStar, s, profile.alpha);
            manager.priceModifiers[def] = closing;
            Telemetry.Record(def, p0, closing, s, sStar, c0, consumption, production, playerNet, shock); //flagged for later removal
        }
        st.pendingUnits.Clear();
        Telemetry.EndDay(); //flagged for later removal
    }

    private static float PriceRatio(float sStar, float s, float alpha)
    {
        return Mathf.Clamp(Mathf.Pow(sStar / s, alpha),
            MarketTuning.PriceRatioMin, MarketTuning.PriceRatioMax);
    }

    private static float FlowNoise()
    {
        float sigma = MarketTuning.FlowNoiseSigma;
        return Mathf.Exp(sigma * Rand.Gaussian() - 0.5f * sigma * sigma);
    }
}
