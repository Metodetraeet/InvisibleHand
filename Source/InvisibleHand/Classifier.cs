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
        if (def.smallVolume && def.IsStuff)
        {
            return Archetype.PreciousDurable;
        }
        if (def.IsWeapon || def.IsApparel)
        {
            return Archetype.Manufactured;
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
        var ext = def.GetModExtension<MarketProfileExtension>();
        if (ext != null)
        {
            return new MarketProfile(ext.depthDays, ext.alpha, ext.demandElasticity,
                ext.supplyElasticity, ext.drainCap, 0f);
        }
        var profile = MarketProfiles.ByArchetype[Classify(def)];
        if (def.HasComp(typeof(CompRottable))) //perishables get shallower depth
        {
            return new MarketProfile(profile.depthDays * 0.6f, profile.alpha,
                profile.demandElasticity, profile.supplyElasticity,
                profile.drainCap, profile.budgetShare);
        }
        return profile;
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
        sb.AppendLine("defName;label;modSource;archetype;marketValue;depthDays;alpha;demandElasticity;supplyElasticity;drainCap");
        foreach (var def in items.OrderBy(d => Classify(d).ToString()).ThenBy(d => d.defName))
        {
            var p = ProfileFor(def);
            sb.AppendLine(string.Join(";",
                def.defName,
                def.label,
                def.modContentPack?.Name ?? "unknown",
                Classify(def).ToString(),
                def.BaseMarketValue.ToString("F2"),
                p.depthDays.ToString("F0"),
                p.alpha.ToString("F2"),
                p.demandElasticity.ToString("F2"),
                p.supplyElasticity.ToString("F2"),
                p.drainCap.ToString("F2")));
        }
        var path = Path.Combine(GenFilePaths.SaveDataFolderPath, "InvisibleHand_Classification.csv");
        File.WriteAllText(path, sb.ToString());
        Log.Message("[Invisible Hand] Full report written to " + path);
    }
}
