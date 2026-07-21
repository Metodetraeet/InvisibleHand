using System.IO;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse;

namespace InvisibleHand;

//Logging for balancing and tuning - remove later//
public static class Telemetry
{
    public const int SchemaVersion = 4;
    public static bool Enabled = true;

    private const string ItemsHeader =
        "schema;gameId;sessionId;day;defName;archetype;p0;price;priceRatio;stock;sStar;stockRatio;c0;consumption;production;playerNet;demandShock;newsShock";
    private const string WorldHeader =
        "schema;gameId;sessionId;day;tick;activity;baselineActivity;activityRatio;worldFlow;universeCount";

    private static StringBuilder itemRows;
    private static string gameId;
    private static string sessionId;

    public static void NewSession()
    {
        sessionId = System.DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssZ");
    }
    private static int day;

    private static string ItemsPath => Path.Combine(GenFilePaths.SaveDataFolderPath, $"InvisibleHand_Telemetry_Items_v{SchemaVersion}.csv");
    private static string WorldPath => Path.Combine(GenFilePaths.SaveDataFolderPath, $"InvisibleHand_Telemetry_World_v{SchemaVersion}.csv");

    public static void BeginDay(MarketState st)
    {
        if (!Enabled)
        {
            return;
        }
        gameId = $"{Find.World.info.seedString}-{Find.World.info.persistentRandomValue}";
        sessionId ??= System.DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssZ"); // defensive; FinalizeInit normally mints
        day = Find.TickManager.TicksGame / GenDate.TicksPerDay;
        itemRows = new StringBuilder(256 * st.universe.Count);
 
        float activity = MarketState.CurrentActivity();
        float ratio = st.BaselineActivity > 0f ? activity / st.BaselineActivity : 1f;
        string worldRow = string.Join(";",
            SchemaVersion, gameId, sessionId, day, Find.TickManager.TicksGame,
            activity.ToString("F0"), st.BaselineActivity.ToString("F0"),
            ratio.ToString("F3"), st.worldFlow.ToString("F0"), st.universe.Count);
        Append(WorldPath, WorldHeader, worldRow + "\n");
    }

    public static void Record(ThingDef def, float p0, float price, float stock, float sStar, float c0, float consumption, float production, float playerNet, float demandShock, float newsShock)
    {
        if (!Enabled || itemRows == null)
        {
            return;
        }
        itemRows
            .Append(SchemaVersion).Append(';')
            .Append(gameId).Append(';')
            .Append(sessionId).Append(';')
            .Append(day).Append(';')
            .Append(def.defName).Append(';')
            .Append(Classifier.Classify(def)).Append(';')
            .Append(p0.ToString("F2")).Append(';')
            .Append(price.ToString("F3")).Append(';')
            .Append((price / p0).ToString("F4")).Append(';')
            .Append(stock.ToString("F1")).Append(';')
            .Append(sStar.ToString("F1")).Append(';')
            .Append((stock / sStar).ToString("F4")).Append(';')
            .Append(c0.ToString("F3")).Append(';')
            .Append(consumption.ToString("F2")).Append(';')
            .Append(production.ToString("F2")).Append(';')
            .Append(playerNet.ToString("F1")).Append(';')
            .Append(demandShock.ToString("F4")).Append(';')
            .Append(newsShock.ToString("F4"))
            .Append('\n');
    }

    public static void EndDay()
    {
        if (!Enabled || itemRows == null)
        {
            return;
        }
        Append(ItemsPath, ItemsHeader, itemRows.ToString());
        itemRows = null;
    }

    private static void Append(string path, string header, string content)
    {
        try
        {
            if (!File.Exists(path))
            {
                File.AppendAllText(path, "sep=;\n" + header + "\n");
            }
            File.AppendAllText(path, content);
        }
        catch (IOException e)
        {
            Log.WarningOnce($"[Invisible Hand] Telemetry write failed (file open in another program?): {e.Message}",
                path.GetHashCode());
        }
    }

    [DebugAction("Invisible Hand", "Toggle telemetry",
        allowedGameStates = AllowedGameStates.Playing)]
    private static void Toggle()
    {
        Enabled = !Enabled;
        Log.Message($"[Invisible Hand] Telemetry {(Enabled ? "ON" : "OFF")} — files in {GenFilePaths.SaveDataFolderPath}");
    }
}
