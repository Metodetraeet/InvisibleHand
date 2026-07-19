using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LudeonTK;
using RimWorld;
using VanillaTradingExpanded;
using Verse;

namespace InvisibleHand;

public static class Classifier
{
    public const float MaxSaneMarketValue = 50_000f; //sanity check for market value. 
    private static readonly Dictionary<ThingDef, Archetype> cache = new();

    public static Archetype Classify(ThingDef def)
    {
        if (cache.TryGetValue(def, out var cached))
        {
            return cached;
        }
        var result = ClassifyInner(def);
        cache[def] = result;
        return result;
    }

    private static Archetype ClassifyInner(ThingDef def)
    {
        //order is significant. First match wins
        var ext = def.GetModExtension<MarketProfileExtension>();
        if (ext != null && ext.archetype != Archetype.Unset)
        {
            return ext.archetype;
        }
        if (VanillaMarketValue(def) > MaxSaneMarketValue)
        {
            return Archetype.Excluded;
        }
        if (def.race != null)
        {
            if (def.race.Animal) return Archetype.Livestock;
            if (def.race.Humanlike) return Archetype.Slaves;
            return Archetype.Excluded;
        }
        if (def.IsDrug)
        {
            return def.ingestible?.drugCategory == DrugCategory.Medical
                ? Archetype.Medical
                : Archetype.LuxuryConsumable;
        }
        if (def.IsMedicine)
        {
            return Archetype.Medical;
        }
        if (def.IsNutritionGivingIngestible)
        {
            return Archetype.Food;
        }
        if (def.tradeTags != null)
        {
            if (def.tradeTags.Contains("Artifact") || def.tradeTags.Contains("ExoticMisc"))
            {
                return Archetype.Collectible;
            }
            if (def.tradeTags.Contains("TechHediff"))
            {
                return Archetype.Manufactured;
            }
        }
        if (def.smallVolume && def.IsStuff)
        {
            return Archetype.PreciousDurable;
        }
        if (def.IsWeapon || def.IsApparel)
        {
            return Archetype.Manufactured;
        }
        if (def.category == ThingCategory.Building)
        {
            return def.thingCategories?.Contains(ThingCategoryDefOf.BuildingsArt) == true
                ? Archetype.Collectible
                : Archetype.Manufactured;
        }
        if (def.CountAsResource || def.IsStuff)
        {
            return Archetype.RawMaterial;
        }
        if (def.HasComp(typeof(CompQuality)) && def.stackLimit == 1)
        {
            return Archetype.Collectible;
        }
        return Archetype.General;
    }

    public static MarketProfile ProfileFor(ThingDef def)
    {
        var archetype = Classify(def);
        if (archetype == Archetype.Excluded)
        {
            Log.ErrorOnce($"[Invisible Hand] ProfileFor called on excluded def {def.defName}", def.shortHash);
            return MarketProfiles.ByArchetype[Archetype.General];
        }
        var p = MarketProfiles.ByArchetype[archetype];
        float depth = p.depthDays;
        float alpha = p.alpha;
        float ed = p.demandElasticity;
        float es = p.supplyElasticity;
        float cap = p.drainCap;
 
        if (def.HasComp(typeof(CompRottable)))
        {
            depth *= 0.6f; //perishables get shallower depth
        }
 
        var ext = def.GetModExtension<MarketProfileExtension>();
        if (ext != null)
        {
            if (ext.depthDays >= 0f) depth = ext.depthDays;
            if (ext.alpha >= 0f) alpha = ext.alpha;
            if (ext.demandElasticity >= 0f) ed = ext.demandElasticity;
            if (ext.supplyElasticity >= 0f) es = ext.supplyElasticity;
            if (ext.drainCap >= 0f) cap = ext.drainCap;
        }
        return new MarketProfile(depth, alpha, ed, es, cap);
    }

    public static float VanillaMarketValue(ThingDef def) //temporarry home. Borrowed from VTE authors
    {
        StatWorker_GetBaseValueFor_Patch.outputOnlyVanilla = true;
        try
        {
            return def.GetStatValueAbstract(StatDefOf.MarketValue);
        }
        finally
        {
            StatWorker_GetBaseValueFor_Patch.outputOnlyVanilla = false;
        }
    }

    public static List<ThingDef> BuildUniverse() //temporary home
    {
        return Utils.cachedTradeableItems
            .Where(d => Classify(d) != Archetype.Excluded)
            .ToList();
    }

    [DebugAction("Invisible Hand", "Dump market classification",
    allowedGameStates = AllowedGameStates.Playing)]
    private static void DumpClassification()
    {
        var items = Utils.cachedTradeableItems;
        var counts = items.GroupBy(Classify)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key}: {g.Count()}");
        Log.Message("[Invisible Hand] Classified " + items.Count + " tradeables — " +
            string.Join(", ", counts));

        var sb = new StringBuilder();
        sb.AppendLine("sep=;");
        sb.AppendLine("defName;label;modSource;category;archetype;marketValue;depthDays;alpha;demandElasticity;supplyElasticity;drainCap");
        foreach (var def in items.OrderBy(d => Classify(d).ToString()).ThenBy(d => d.defName))
        {
            var archetype = Classify(def);
            string profileCols;
            if (archetype == Archetype.Excluded)
            {
                profileCols = ";;;;";
            }
            else
            {
                var p = ProfileFor(def);
                profileCols = string.Join(";",
                    p.depthDays.ToString("F0"),
                    p.alpha.ToString("F2"),
                    p.demandElasticity.ToString("F2"),
                    p.supplyElasticity.ToString("F2"),
                    p.drainCap.ToString("F2"));
            }
            sb.AppendLine(string.Join(";",
                def.defName,
                def.label,
                def.modContentPack?.Name ?? "unknown",
                def.category.ToString(),
                archetype.ToString(),
                VanillaMarketValue(def).ToString("F2"),
                profileCols));
        }
        var path = Path.Combine(GenFilePaths.SaveDataFolderPath, "InvisibleHand_Classification.csv");
        File.WriteAllText(path, sb.ToString());
        Log.Message("[Invisible Hand] Full report written to " + path);
    }
}