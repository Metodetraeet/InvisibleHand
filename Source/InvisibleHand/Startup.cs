using HarmonyLib;
using Verse;

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
