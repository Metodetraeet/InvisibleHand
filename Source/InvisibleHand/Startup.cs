using HarmonyLib;
using VanillaTradingExpanded;
using Verse;
using RimWorld;

namespace InvisibleHand;

[StaticConstructorOnStartup]
public static class Startup
{
    static Startup()
    {
         var harmony = new Harmony("metodetraeet.InvisibleHand");
            harmony.PatchAll();
            Log.Message("[Invisible Hand] Loaded, patches applied.");
        }
    }

[HarmonyPatch(typeof(TradingManager), nameof(TradingManager.RegisterSoldThing))]
public static class TradingManager_RegisterSoldThing_Patch
 {
        public static void Postfix(Thing soldThing, int countToSell)
      {
           Log.Message($"[Invisible Hand] Sold {countToSell}x {soldThing.def.defName} " +
                     $"(base value {soldThing.def.GetStatValueAbstract(RimWorld.StatDefOf.MarketValue):F2})");
     }
}
