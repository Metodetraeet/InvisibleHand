using System.Collections.Generic;
using System.Text;
using RimWorld;
using VanillaTradingExpanded;
using Verse;
using LudeonTK;

namespace InvisibleHand;

public class MarketState : GameComponent
{
    public static MarketState Instance;

    public Dictionary<ThingDef, float> stock = new();
    public Dictionary<ThingDef, float> pendingUnits = new();

    private List<ThingDef> stockKeys;
    private List<float> stockValues;
    private List<ThingDef> pendingKeys;
    private List<float> pendingValues;

    public MarketState(Game game)
    {
        Instance = this;
    }

    
    //units: positive = player sold (supply into market), negative = player bought.
    //sign is set by the caller in Patches.cs.
    public void RegisterTrade(ThingDef def, float units)
    {
        if (def == null
            || Utils.tradeableItemsToIgnore.Contains(def)
            || Classifier.Classify(def) == Archetype.Excluded)
        {
            return;
        }
        pendingUnits.TryGetValue(def, out var current);
        pendingUnits[def] = current + units;
    }
    

    public override void GameComponentTick()
    {
        if (Find.TickManager.TicksGame % GenDate.TicksPerDay != 0 || pendingUnits.Count == 0)
        {
            return;
        }
        var sb = new StringBuilder("[Invisible Hand] Daily buffer:");
        foreach (var kvp in pendingUnits)
        {
            sb.Append($" {kvp.Key.defName}={kvp.Value:F0}");
        }
        Log.Message(sb.ToString());
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref stock, "stock", LookMode.Def, LookMode.Value, ref stockKeys, ref stockValues);
        Scribe_Collections.Look(ref pendingUnits, "pendingUnits", LookMode.Def, LookMode.Value, ref pendingKeys, ref pendingValues);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            stock ??= new Dictionary<ThingDef, float>();
            pendingUnits ??= new Dictionary<ThingDef, float>();
            //drop entries whose ThingDef no longer resolves. Likely caused by mod removals.
            stock.RemoveAll(kvp => kvp.Key == null);
            pendingUnits.RemoveAll(kvp => kvp.Key == null);
            stock.RemoveAll(kvp => Classifier.Classify(kvp.Key) == Archetype.Excluded);
            pendingUnits.RemoveAll(kvp => Classifier.Classify(kvp.Key) == Archetype.Excluded);
        }
    }

    [DebugAction("Invisible Hand", "Dump pending trade buffer",
    allowedGameStates = AllowedGameStates.PlayingOnMap)]
    private static void DumpPendingBuffer()
    {
        if (Instance == null || Instance.pendingUnits.Count == 0)
        {
            Log.Message("[Invisible Hand] Pending buffer is empty.");
            return;
        }
        var sb = new StringBuilder("[Invisible Hand] Pending buffer:");
        foreach (var kvp in Instance.pendingUnits)
        {
            sb.Append($" {kvp.Key.defName}={kvp.Value:F0}");
        }
        Log.Message(sb.ToString());
    }
}
