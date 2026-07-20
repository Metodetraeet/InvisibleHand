using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using VanillaTradingExpanded;
using Verse;

namespace InvisibleHand;

[HarmonyPatch(typeof(TransferableUIUtility), nameof(TransferableUIUtility.DoCountAdjustInterface))]
public static class TransferableUIUtility_DoCountAdjustInterface_Patch
{
    public static void Postfix(Rect rect, Transferable trad)
    {
        // Caravan/loading screens pass TransferableOneWay - the type filter
        // limits us to actual trade sessions.
        if (trad is not Tradeable tradeable || tradeable.ThingDef == null
            || tradeable.IsCurrency || !Mouse.IsOver(rect))
        {
            return;
        }
        TradeSparkline.Draw(tradeable.ThingDef);
    }
}

public static class TradeSparkline
{
    private const int WindowDays = 60;
    private const float W = 240f, H = 130f, Pad = 8f;
    private static readonly List<float> points = new();

    public static void Draw(ThingDef def)
    {
        var manager = TradingManager.Instance;
        if (manager?.priceHistoryRecorders == null
            || !manager.priceHistoryRecorders.TryGetValue(def, out var recorder)
            || recorder?.records == null || recorder.records.Count < 2)
        {
            return;
        }

        points.Clear();
        int start = Mathf.Max(0, recorder.records.Count - WindowDays);
        for (int i = start; i < recorder.records.Count; i++)
        {
            points.Add(recorder.records[i]);
        }

        float p0 = Classifier.VanillaMarketValue(def);
        float current = points[points.Count - 1];

        Vector2 mouse = UI.MousePositionOnUIInverted;
        var winRect = new Rect(
            Mathf.Min(mouse.x + 18f, UI.screenWidth - W - 4f),
            Mathf.Min(mouse.y + 18f, UI.screenHeight - H - 4f), W, H);

        Find.WindowStack.ImmediateWindow(
            683_000 + def.shortHash, winRect, WindowLayer.Super, () =>
        {
            var inner = new Rect(Pad, Pad, W - 2 * Pad, H - 2 * Pad);
            float labelH = 18f;
            var plot = new Rect(inner.x, inner.y + labelH, inner.width, inner.height - 2 * labelH);

            float lo = Mathf.Min(points.Min(), p0);
            float hi = Mathf.Max(points.Max(), p0);
            if (hi - lo < 1e-4f) { hi = lo + 1e-4f; }
            float pad = (hi - lo) * 0.1f;
            lo -= pad; hi += pad;

            float Y(float v) => plot.yMax - (v - lo) / (hi - lo) * plot.height;
            float X(int i) => plot.x + i / (float)(points.Count - 1) * plot.width;

            Text.Font = GameFont.Tiny;
            float delta = p0 > 0f ? (current / p0 - 1f) * 100f : 0f;
            GUI.color = delta < -1f ? Color.green : delta > 1f ? new Color(1f, 0.4f, 0.35f) : Color.gray;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, labelH),
                $"Market price: {current:F2} ({delta:+0.#;-0.#;0}%)");
            GUI.color = Color.white;

            //vanilla reference line
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Widgets.DrawLineHorizontal(plot.x, Y(p0), plot.width);
            GUI.color = Color.white;

            //price polyline
            for (int i = 1; i < points.Count; i++)
            {
                Widgets.DrawLine(new Vector2(X(i - 1), Y(points[i - 1])),
                    new Vector2(X(i), Y(points[i])), ColoredText.CurrencyColor, 1.5f);
            }

            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            Widgets.Label(new Rect(inner.x, plot.yMax + 2f, inner.width, labelH),
                $"{points.Count} days · vanilla base: {p0:F2}");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }, doBackground: true, absorbInputAroundWindow: false);
    }
}