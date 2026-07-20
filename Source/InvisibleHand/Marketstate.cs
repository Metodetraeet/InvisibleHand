using System.Collections.Generic;
using System.Text;
using RimWorld;
using VanillaTradingExpanded;
using Verse;
using LudeonTK;
using RimWorld.Planet;
using UnityEngine;

namespace InvisibleHand;

public class MarketState : GameComponent
{
    public static MarketState Instance;

    //scribed state
    public Dictionary<ThingDef, float> stock = new();
    public Dictionary<ThingDef, float> pendingUnits = new();
    private float baselineActivity;
    private bool engineActive;
    public float BaselineActivity => baselineActivity; //flagged for later removal

    //derived state
    public List<ThingDef> universe = new();
    public Dictionary<ThingDef, float> c0Units = new();
    public float worldFlow; //abstraction of total market activity in the world

    private List<ThingDef> stockKeys;
    private List<float> stockValues;
    private List<ThingDef> pendingKeys;
    private List<float> pendingValues;

    public MarketState(Game game)
    {
        Instance = this;
    }

    public override void FinalizeInit()
    {
        base.FinalizeInit();
        universe = Classifier.BuildUniverse();
        Telemetry.NewSession();
        float activity = CurrentActivity();
        if (baselineActivity <= 0f)
        {
            baselineActivity = activity;
        }
        ComputeFlows(activity);
        InitializeStocks();
        if (!engineActive)
        {
            pendingUnits.Clear();
            engineActive = true;
        }
    }

    public static float CurrentActivity()
    {
        float sum = 0f;
        foreach (Settlement s in Find.WorldObjects.Settlements)
        {
            if (s.Faction == null || s.Faction.IsPlayer)
            {
                continue;
            }
            sum += ActivityWeight(s.Faction.def.techLevel);
        }
        return sum;
    }

    private static float ActivityWeight(TechLevel tech)
    {
        switch (tech)
        {
            case TechLevel.Medieval: return 2f;
            case TechLevel.Industrial: return 4f;
            case TechLevel.Spacer: return 7f;
            case TechLevel.Ultra: return 10f;
            case TechLevel.Archotech: return 10f;
            default: return 1f;
        }
    }

    private void ComputeFlows(float activity)
    {
        float ratio = baselineActivity > 0f
            ? Mathf.Clamp(activity / baselineActivity,
                MarketTuning.ActivityRatioMin, MarketTuning.ActivityRatioMax)
            : 1f;
        worldFlow = MarketTuning.ReferenceWorldFlow * ratio;
 
        var members = new Dictionary<Archetype, int>();
        foreach (var def in universe)
        {
            var a = Classifier.Classify(def);
            members.TryGetValue(a, out var n);
            members[a] = n + 1;
        }
 
        c0Units.Clear();
        foreach (var def in universe)
        {
            var a = Classifier.Classify(def);
            if (!MarketProfiles.BudgetShareByArchetype.TryGetValue(a, out var share))
            {
                continue;
            }
            float p0 = Classifier.VanillaMarketValue(def);
            if (p0 <= 0f)
            {
                continue;
            }
            float perItemSilver = worldFlow * share / Mathf.Sqrt(members[a]);
            c0Units[def] = perItemSilver / p0;
        }
    }
    
    private void InitializeStocks()
    {
        var manager = TradingManager.Instance;
        foreach (var def in universe)
        {
            if (stock.ContainsKey(def))
            {
                continue;
            }
            if (!c0Units.TryGetValue(def, out var c0))
            {
                continue;
            }
            var profile = Classifier.ProfileFor(def);
            float sStar = profile.depthDays * c0;
            float p0 = Classifier.VanillaMarketValue(def);
 
            //adopting VTE's current price (history)as the initial condition
            float current = p0;
            if (manager != null && manager.TryGetModifiedPriceFor(def, out var vtePrice))
            {
                current = vtePrice;
            }
            float ratio = Mathf.Clamp(current / p0,
                MarketTuning.InitPriceRatioMin, MarketTuning.InitPriceRatioMax);
            //invert p = p0 * (S*/S)^alpha for S: the stock that explains the price.
            stock[def] = sStar * Mathf.Pow(1f / ratio, 1f / profile.alpha);
        }
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
        if (Find.TickManager.TicksGame % GenDate.TicksPerDay != 0)
        {
            return;
        }
        //changes in number of settlements impact maket depth daily
        ComputeFlows(CurrentActivity());
        if (pendingUnits.Count > 0)
        {
            var sb = new StringBuilder("[Invisible Hand] Daily buffer:");
            foreach (var kvp in pendingUnits)
            {
                sb.Append($" {kvp.Key.defName}={kvp.Value:F0}");
            }
            Log.Message(sb.ToString());
        }
        MarketEngine.Tick(this);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref stock, "stock", LookMode.Def, LookMode.Value, ref stockKeys, ref stockValues);
        Scribe_Collections.Look(ref pendingUnits, "pendingUnits", LookMode.Def, LookMode.Value, ref pendingKeys, ref pendingValues);
        Scribe_Values.Look(ref baselineActivity, "baselineActivity", 0f);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            stock ??= new Dictionary<ThingDef, float>();
            pendingUnits ??= new Dictionary<ThingDef, float>();
            stock.RemoveAll(kvp => kvp.Key == null);
            pendingUnits.RemoveAll(kvp => kvp.Key == null);
            stock.RemoveAll(kvp => Classifier.Classify(kvp.Key) == Archetype.Excluded);
            pendingUnits.RemoveAll(kvp => Classifier.Classify(kvp.Key) == Archetype.Excluded);
        }
    }

    //debug actions below//

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

    [DebugAction("Invisible Hand", "Market status",
        allowedGameStates = AllowedGameStates.PlayingOnMap)]
    private static void MarketStatus()
    {
        if (Instance == null)
        {
            return;
        }
        float activity = CurrentActivity();
        float ratio = Instance.baselineActivity > 0f ? activity / Instance.baselineActivity : 1f;
        Log.Message("[Invisible Hand] Market status:\n" +
            $"  activity={activity:F0}  baseline={Instance.baselineActivity:F0}  ratio={ratio:F2} " +
            $"(clamped {MarketTuning.ActivityRatioMin}-{MarketTuning.ActivityRatioMax})\n" +
            $"  worldFlow={Instance.worldFlow:F0} silver/day  universe={Instance.universe.Count} defs  " +
            $"stocked={Instance.stock.Count}  buffered={Instance.pendingUnits.Count}");
    }
 
    [DebugAction("Invisible Hand", "Reset market state",
        allowedGameStates = AllowedGameStates.PlayingOnMap)]
    private static void ResetMarketState()
    {
        if (Instance == null)
        {
            return;
        }
        Instance.stock.Clear();
        Instance.pendingUnits.Clear();
        Instance.ComputeFlows(CurrentActivity());
        Instance.InitializeStocks();
        Log.Message("[Invisible Hand] Market state cleared and re-initialized from current prices. " +
            "(For a true vanilla reset, use VTE's 'Reset price changes' first, then this.)");
    }
}


