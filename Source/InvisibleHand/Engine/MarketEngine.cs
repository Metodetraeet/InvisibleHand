using UnityEngine;
using VanillaTradingExpanded;
using Verse;
using RimWorld;

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
        int day = Find.TickManager.TicksGame / GenDate.TicksPerDay;
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

            //ambient demand shock. Persistent good-months and bad-months. 
            st.demandShock.TryGetValue(def, out var shock);
            shock = MarketTuning.ShockRho * shock
                + MarketTuning.ShockSigma * (float)MarketRandom.Gaussian(
                    def, day, MarketRandomStream.AmbientDemandShock);
            st.demandShock[def] = shock;
            st.newsShock.TryGetValue(def, out var news);
            if (news != 0f)
            {
                news *= MarketTuning.NewsShockRho;
                if (Mathf.Abs(news) < 1e-4f)
                {
                    news = 0f;
                    st.newsShock.Remove(def);
                }
                else
                {
                    st.newsShock[def] = news;
                }
            }
            float demandMult = Mathf.Exp(shock + news - MarketTuning.ShockMeanCorrection);

            //log-normal flow noise, mean exactly 1, so noise adds texture without adding drift
            float consumption = c0
                * Mathf.Min(Mathf.Pow(rel, -profile.demandElasticity), profile.drainCap)
                * demandMult
                * FlowNoise((float)MarketRandom.Gaussian(
                    def, day, MarketRandomStream.MarketOutflowNoise));
            float production = c0
                * Mathf.Pow(rel, profile.supplyElasticity)
                * FlowNoise((float)MarketRandom.Gaussian(
                    def, day, MarketRandomStream.MarketInflowNoise));

            st.pendingUnits.TryGetValue(def, out var playerNet);

            s = Mathf.Max(s + production - consumption + playerNet,
                sStar * MarketTuning.StockFloorFraction);
            if (float.IsNaN(s) || float.IsInfinity(s))
            {
                Log.ErrorOnce($"[Invisible Hand] Non-finite stock computed for {def.defName}. Resetting to equilibrium.", def.shortHash ^ 0x0F17E);
                s = sStar;
            }
            st.stock[def] = s;

            float closing = p0 * PriceRatio(sStar, s, profile.alpha);
            if (float.IsNaN(closing) || float.IsInfinity(closing))
            {
                continue;
            }
            manager.priceModifiers[def] = closing;
            Telemetry.Record(def, p0, closing, s, sStar, c0, consumption, production, playerNet, shock, news); //flagged for later removal
        }
        st.pendingUnits.Clear();
        Telemetry.EndDay(); //flagged for later removal
    }

    private static float PriceRatio(float sStar, float s, float alpha)
    {
        return Mathf.Clamp(Mathf.Pow(sStar / s, alpha),
            MarketTuning.PriceRatioMin, MarketTuning.PriceRatioMax);
    }

    private static float FlowNoise(float gaussian)
    {
        float sigma = MarketTuning.FlowNoiseSigma;
        return Mathf.Exp(sigma * gaussian - 0.5f * sigma * sigma);
    }
}