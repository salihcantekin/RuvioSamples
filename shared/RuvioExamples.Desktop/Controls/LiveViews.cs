using System.Globalization;
using RuvioExamples.LoadKit;

namespace RuvioExamples.Desktop.Controls;

/// <summary>Stacked bars, one per second, for the last minute: green let through, red limited, amber errors.</summary>
internal sealed class TimelineChart : Surface
{
    private const int Slots = 60;
    private IReadOnlyList<SecondBucket> _seconds = [];

    public string SuccessLabel { get; set; } = "ok";

    public string RejectedLabel { get; set; } = "rejected";

    public void Update(IReadOnlyList<SecondBucket> seconds)
    {
        _seconds = seconds;
        Invalidate();
    }

    protected override void Draw(Graphics g)
    {
        DrawText(g, "Requests per second, last 60 s", Theme.BodyBold, Theme.Text, 0, 0);

        float legend = Width;
        foreach (var (label, color) in new[] { ("503 / error", Theme.Warn), (RejectedLabel, Theme.Rejected), (SuccessLabel, Theme.Success) })
        {
            var size = g.MeasureString(label, Theme.Small);
            legend -= size.Width + 4;
            DrawText(g, label, Theme.Small, Theme.Muted, legend, 3);
            Fill(g, color, new RectangleF(legend - 14, 7, 9, 9), 2);
            legend -= 26;
        }

        const float left = 34, bottom = 20, top = 30;
        float plotW = Width - left - 2, plotH = Height - bottom - top;

        if (plotW < 60 || plotH < 40)
            return;

        var visible = _seconds.Count > Slots ? _seconds.Skip(_seconds.Count - Slots).ToArray() : [.. _seconds];
        int peak = Math.Max(5, visible.Length == 0 ? 0 : visible.Max(s => s.Total));
        int step = peak <= 10 ? 2 : peak <= 30 ? 5 : peak <= 100 ? 20 : peak <= 400 ? 100 : 500;
        int max = ((peak / step) + 1) * step;

        using var gridPen = new Pen(Theme.Grid);

        for (int value = 0; value <= max; value += step)
        {
            float y = top + plotH - (plotH * value / max);
            g.DrawLine(gridPen, left, y, left + plotW, y);
            DrawText(g, value.ToString(CultureInfo.InvariantCulture), Theme.Small, Theme.Muted, left - 6, y - 8, right: true);
        }

        float slot = plotW / Slots;
        float barWidth = Math.Max(2, slot * 0.78f);

        for (int i = 0; i < visible.Length; i++)
        {
            var bucket = visible[i];
            float x = left + ((Slots - visible.Length + i) * slot) + ((slot - barWidth) / 2);
            float y = top + plotH;

            y = Stack(g, Theme.Success, x, y, barWidth, plotH, bucket.Success, max);
            y = Stack(g, Theme.Rejected, x, y, barWidth, plotH, bucket.Rejected, max);
            Stack(g, Theme.Warn, x, y, barWidth, plotH, bucket.Unavailable + bucket.Error, max);
        }

        DrawText(g, "-60s", Theme.Small, Theme.Muted, left, Height - 17);
        DrawText(g, "-30s", Theme.Small, Theme.Muted, left + (plotW / 2) - 12, Height - 17);
        DrawText(g, "now", Theme.Small, Theme.Muted, left + plotW - 22, Height - 17);
    }

    private static float Stack(Graphics g, Color color, float x, float yBottom, float width, float plotH, int count, int max)
    {
        if (count <= 0)
            return yBottom;

        float h = Math.Max(2, plotH * count / max);
        Fill(g, color, new RectangleF(x, yBottom - h, width, h), 1.5f);

        return yBottom - h - 1;
    }
}

/// <summary>One row per virtual client. Every request is a small block, newest on the right.</summary>
internal sealed class LaneStrips : Surface
{
    private const float LaneHeight = 30, LabelWidth = 100, CounterWidth = 124, Cell = 6, Gap = 2;
    private IReadOnlyList<LaneSnapshot> _lanes = [];

    public void Update(IReadOnlyList<LaneSnapshot> lanes)
    {
        bool resize = lanes.Count != _lanes.Count;
        _lanes = lanes;

        if (resize)
            Height = (int)Math.Max(LaneHeight * 3, lanes.Count * LaneHeight);

        Invalidate();
    }

    protected override void Draw(Graphics g)
    {
        float stripLeft = LabelWidth, stripRight = Width - CounterWidth;
        int capacity = (int)Math.Max(0, (stripRight - stripLeft) / (Cell + Gap));

        for (int lane = 0; lane < _lanes.Count; lane++)
        {
            var item = _lanes[lane];
            float top = lane * LaneHeight, mid = top + (LaneHeight / 2);

            Fill(g, Theme.Grid, new RectangleF(stripLeft - 4, top + 3, Math.Max(0, stripRight - stripLeft + 4), LaneHeight - 6), 5);
            DrawText(g, item.ClientId, Theme.Mono, Theme.Text, 0, mid - 9);

            int skip = Math.Max(0, item.Recent.Length - capacity);

            for (int i = skip; i < item.Recent.Length; i++)
            {
                float x = stripRight - ((item.Recent.Length - i) * (Cell + Gap));
                Fill(g, Theme.ForOutcome(item.Recent[i]), new RectangleF(x, mid - 7, Cell, 14), 1.5f);
            }

            DrawText(g, item.Success.ToString(CultureInfo.InvariantCulture), Theme.MonoBold, Theme.Success, stripRight + 10, mid - 9);
            DrawText(g, item.Rejected.ToString(CultureInfo.InvariantCulture), Theme.MonoBold, Theme.Rejected, stripRight + 54, mid - 9);

            if (item.Other > 0)
                DrawText(g, item.Other.ToString(CultureInfo.InvariantCulture), Theme.MonoBold, Theme.Warn, stripRight + 92, mid - 9);
        }
    }
}

/// <summary>"Served by": how many allowed requests each API instance answered.</summary>
internal sealed class InstanceBars : Surface
{
    private IReadOnlyDictionary<string, long> _counts = new Dictionary<string, long>();

    public void Update(IReadOnlyDictionary<string, long> counts)
    {
        if (counts.Count != _counts.Count)
            Height = 28 + (Math.Max(1, counts.Count) * 34);

        _counts = counts;
        Invalidate();
    }

    protected override void Draw(Graphics g)
    {
        DrawText(g, "Served by", Theme.BodyBold, Theme.Text, 0, 0);

        if (_counts.Count == 0)
        {
            DrawText(g, "no instance has answered yet", Theme.Small, Theme.Muted, 0, 30);
            return;
        }

        long total = Math.Max(1, _counts.Values.Sum());
        float y = 30;

        foreach (var (name, count) in _counts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            DrawText(g, name, Theme.Small, Theme.Text, 0, y - 2);
            DrawText(g, count.ToString(CultureInfo.InvariantCulture), Theme.MonoSmall, Theme.Muted, Width - 6, y - 2, right: true);
            Fill(g, Theme.Line, new RectangleF(0, y + 16, Width, 6), 3);
            Fill(g, Theme.Accent, new RectangleF(0, y + 16, Math.Max(6, Width * count / total), 6), 3);
            y += 34;
        }
    }
}

/// <summary>The most recent responses, newest first.</summary>
internal sealed class LogView : Surface
{
    private IReadOnlyList<RequestResult> _recent = [];

    public void Update(IReadOnlyList<RequestResult> recent)
    {
        _recent = recent;
        Invalidate();
    }

    protected override void Draw(Graphics g)
    {
        DrawText(g, "Last responses", Theme.BodyBold, Theme.Text, 0, 0);

        float y = 28;

        foreach (var result in _recent.Reverse().Take((Height - 28) / 20))
        {
            string detail = result.Error ?? (result.RetryAfterSeconds is { } retry
                ? $"{result.Instance} · retry {retry:0.#}s"
                : result.Instance ?? string.Empty);

            DrawText(g, result.StatusCode == 0 ? "ERR" : result.StatusCode.ToString(CultureInfo.InvariantCulture),
                Theme.MonoBold, Theme.ForOutcome(result.Outcome), 0, y);
            DrawText(g, result.ClientId, Theme.MonoSmall, Theme.Text, 40, y + 1);
            DrawText(g, $"{result.LatencyMs:0} ms", Theme.MonoSmall, Theme.Muted, 126, y + 1);
            DrawText(g, detail.Length > 22 ? detail[..22] : detail, Theme.MonoSmall, Theme.Muted, 188, y + 1);
            y += 20;
        }
    }
}
