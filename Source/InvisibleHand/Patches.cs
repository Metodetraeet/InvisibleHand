using HarmonyLib;
using VanillaTradingExpanded;
using Verse;

namespace InvisibleHand;

[HarmonyPatch(typeof(TradingManager), nameof(TradingManager.RegisterSoldThing))]
public static class TradingManager_RegisterSoldThing_Patch
{
    public static void Postfix(Thing soldThing, int countToSell)
    {
        MarketState.Instance?.RegisterTrade(soldThing.def, countToSell);
    }
}


[HarmonyPatch(typeof(TradingManager), nameof(TradingManager.RegisterPurchasedThing))]
public static class TradingManager_RegisterPurchasedThing_Patch
{
    public static void Postfix(Thing soldThing, int countToSell)
    {
        MarketState.Instance?.RegisterTrade(soldThing.def, -countToSell);
    }
}

