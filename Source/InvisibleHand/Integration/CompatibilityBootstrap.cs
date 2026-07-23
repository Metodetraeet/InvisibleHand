using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using VanillaTradingExpanded;
using Verse;

namespace InvisibleHand;

//validate every critical target against the loaded VTE, then install all critical patches or none
public static class CompatibilityBootstrap
{
    public const string HarmonyId = "metodetraeet.InvisibleHand";
    public const string CriticalCategory = "InvisibleHandCritical";
    public const string OptionalCategory = "InvisibleHandOptional";

    public static void Run()
    {
        var harmony = new Harmony(HarmonyId);
        try
        {
            //stage 1: validate
            var resolvers = BuildCriticalResolvers();
            var plan = CompatibilityBootstrapCore.Validate(
                resolvers.Keys, name => resolvers[name]());
            if (!plan.InstallCriticals)
            {
                Fail(harmony, rollback: false,
                    "critical target validation failed", plan.Failures);
                return;
            }

            //stage 2: install every critical patch, then verify 
            harmony.PatchCategory(CriticalCategory);
            string verify = VerifySuppressionsInstalled();
            if (verify != null)
            {
                throw new InvalidOperationException(verify);
            }
        }
        catch (Exception e)
        {
            Fail(harmony, rollback: true,
                "critical patch installation failed: " + e.Message, null);
            return;
        }

        CompatibilityStatus.EngineEnabled = true;
        Log.Message("[Invisible Hand] VTE integration validated and installed. Market engine enabled.");

        //optional patches install separately and may fail independently
        InstallOptionals(harmony);
    }

    //signatures pinned against the supported VTE baseline
    private static Dictionary<string, Func<string>> BuildCriticalResolvers()
    {
        return new Dictionary<string, Func<string>>
        {
            ["TradingManager.ProcessPlayerTransactions"] = () =>
                RequireUniqueMethod(typeof(TradingManager), "ProcessPlayerTransactions", Type.EmptyTypes, typeof(void)),
            ["TradingManager.SeasonalPriceUpdates"] = () =>
                RequireUniqueMethod(typeof(TradingManager), "SeasonalPriceUpdates", Type.EmptyTypes, typeof(void)),
            ["TradingManager.SimulateWorldTrading"] = () =>
                RequireUniqueMethod(typeof(TradingManager), "SimulateWorldTrading", Type.EmptyTypes, typeof(void)),
            ["TradingManager.DoPriceRebalances"] = () =>
                RequireUniqueMethod(typeof(TradingManager), "DoPriceRebalances", Type.EmptyTypes, typeof(void)),
            ["TradingManager.RegisterSoldThing"] = () =>
                RequireUniqueMethod(typeof(TradingManager), "RegisterSoldThing",
                    new[] { typeof(Thing), typeof(int) }, typeof(void)),
            ["TradingManager.RegisterPurchasedThing"] = () =>
                RequireUniqueMethod(typeof(TradingManager), "RegisterPurchasedThing",
                    new[] { typeof(Thing), typeof(int) }, typeof(void)),
            ["Tradeable.GetPriceFor"] = () =>
                RequireUniqueMethod(typeof(Tradeable), "GetPriceFor",
                    new[] { typeof(TradeAction) }, typeof(float)),
            ["TradingManager.priceModifiers"] = () =>
                RequireField(typeof(TradingManager), "priceModifiers",
                    typeof(Dictionary<ThingDef, float>), requireStatic: false),
            ["TradingManager.thingsAffectedBySoldPurchasedMarketValue"] = () =>
                RequireField(typeof(TradingManager), "thingsAffectedBySoldPurchasedMarketValue",
                    typeof(Dictionary<ThingDef, float>), requireStatic: false),
            ["TradingManager.Instance"] = () =>
                RequireField(typeof(TradingManager), "Instance",
                    typeof(TradingManager), requireStatic: true),
        };
    }

    private static string RequireUniqueMethod(Type type, string name, Type[] parameters, Type returnType)
    {
        int named = 0;
        foreach (var m in AccessTools.GetDeclaredMethods(type))
        {
            if (m.Name == name)
            {
                named++;
            }
        }
        if (named == 0)
        {
            return "method not found";
        }
        if (named > 1)
        {
            return named + " overloads found, expected exactly one";
        }
        var method = AccessTools.Method(type, name, parameters);
        if (method == null)
        {
            return "parameter types do not match";
        }
        if (method.ReturnType != returnType)
        {
            return "return type is " + method.ReturnType.Name + ", expected " + returnType.Name;
        }
        return null;
    }

    private static string RequireField(Type type, string name, Type fieldType, bool requireStatic)
    {
        var field = AccessTools.Field(type, name);
        if (field == null)
        {
            return "field not found";
        }
        if (field.FieldType != fieldType)
        {
            return "field type is " + field.FieldType.Name + ", expected " + fieldType.Name;
        }
        if (field.IsStatic != requireStatic)
        {
            return requireStatic ? "expected a static field" : "expected an instance field";
        }
        return null;
    }

    private static string VerifySuppressionsInstalled()
    {
        foreach (string name in new[] { "ProcessPlayerTransactions",
            "SeasonalPriceUpdates", "SimulateWorldTrading", "DoPriceRebalances" })
        {
            var method = AccessTools.Method(typeof(TradingManager), name);
            var patches = Harmony.GetPatchInfo(method);
            bool owned = false;
            if (patches != null)
            {
                foreach (var prefix in patches.Prefixes)
                {
                    if (prefix.owner == HarmonyId)
                    {
                        owned = true;
                        break;
                    }
                }
            }
            if (!owned)
            {
                return "suppression prefix missing on TradingManager." + name;
            }
        }
        return null;
    }

    private static void Fail(Harmony harmony, bool rollback, string headline, List<string> failures)
    {
        if (rollback)
        {
            //a partially patched VTE is worse than an unpatched one, so unpatch if necessary
            try
            {
                harmony.UnpatchAll(HarmonyId);
            }
            catch (Exception e)
            {
                Log.Error("[Invisible Hand] Rollback itself failed: " + e);
            }
        }
        CompatibilityStatus.EngineEnabled = false;
        string vteVersion = typeof(TradingManager).Assembly.GetName().Version?.ToString() ?? "unknown";
        string detail = failures != null && failures.Count > 0
            ? "\n  - " + string.Join("\n  - ", failures)
            : string.Empty;
        CompatibilityStatus.FailureSummary = headline + detail;
        Log.Error("[Invisible Hand] " + headline + " (VTE assembly " + vteVersion + ")." + detail
            + "\n[Invisible Hand] The market engine is DISABLED. VTE's original item-price model remains active."
            + " This usually means an unsupported VTE update - await a new Invisible Hand update.");
    }

    public static void ShowFailureDialogOnce()
    {
        if (CompatibilityStatus.EngineEnabled || CompatibilityStatus.FailureDialogShown)
        {
            return;
        }
        CompatibilityStatus.FailureDialogShown = true;
        LongEventHandler.ExecuteWhenFinished(() => Find.WindowStack.Add(new Dialog_MessageBox(
            "Invisible Hand could not take exclusive ownership of Vanilla Trading Expanded's"
            + " physical-item prices, usually because of an unsupported VTE update.\n\n"
            + "The market engine is disabled and VTE's original item-price model remains active.\n\nDetails:\n"
            + (CompatibilityStatus.FailureSummary ?? "see the log"))));
    }

    private static void InstallOptionals(Harmony harmony)
    {
        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(CompatibilityBootstrap).Assembly))
        {
            var category = type.GetCustomAttribute<HarmonyPatchCategory>();
            if (category == null || category.info?.category != OptionalCategory)
            {
                continue;
            }
            try
            {
                new PatchClassProcessor(harmony, type).Patch();
            }
            catch (Exception e)
            {
                Log.Error("[Invisible Hand] Optional integration " + type.Name
                    + " failed to install and is disabled: " + e.Message);
            }
        }
    }
}
