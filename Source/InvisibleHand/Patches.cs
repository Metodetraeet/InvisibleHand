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
    public static void Postfix(Thing soldThing, int countToSell) //soldThing is not a typo (from me). VTE declares RegisterPurchasedThing(Thing soldThing, ...)
    {
        MarketState.Instance?.RegisterTrade(soldThing.def, -countToSell);
    }
}

//overrides VTE's price simulation//

[HarmonyPatch(typeof(TradingManager), "ProcessPlayerTransactions")]
public static class TradingManager_ProcessPlayerTransactions_Patch
{
    public static bool Prefix()
    {
        //with its consumer disabled, VTE's silver buffer would grow forever
        TradingManager.Instance?.thingsAffectedBySoldPurchasedMarketValue?.Clear();
        return false;
    }
}
 
[HarmonyPatch(typeof(TradingManager), "SimulateWorldTrading")]
public static class TradingManager_SimulateWorldTrading_Patch
{
    public static bool Prefix() => false;
}
 
[HarmonyPatch(typeof(TradingManager), "DoPriceRebalances")]
public static class TradingManager_DoPriceRebalances_Patch
{
    public static bool Prefix() => false;
}
 
[HarmonyPatch(typeof(TradingManager), "SeasonalPriceUpdates")]
public static class TradingManager_SeasonalPriceUpdates_Patch
{
    public static bool Prefix() => false;
}
