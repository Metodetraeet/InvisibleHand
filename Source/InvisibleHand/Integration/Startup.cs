using Verse;

namespace InvisibleHand;

[StaticConstructorOnStartup]
public static class Startup
{
    static Startup()
    {
        CompatibilityBootstrap.Run();
    }
}