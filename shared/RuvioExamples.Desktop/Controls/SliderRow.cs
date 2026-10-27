namespace RuvioExamples.Desktop.Controls;

/// <summary>A caption, the current value on the right and a flat slider underneath.</summary>
internal sealed class SliderRow : Surface
{
    private double _value;
    private bool _dragging;

    public SliderRow()
    {
        Height = 46;
        Cursor = Cursors.Hand;
    }

    public string Caption { get; set; } = string.Empty;

    public double Minimum { get; set; }

    public double Maximum { get; set; } = 100;

    public double Step { get; set; } = 1;

    public Func<double, string> Format { get; set; } = value => value.ToString("0");

    public event EventHandler? ValueChanged;

    public double Value
    {
        get => _value;
        set
        {
            double snapped = Math.Round(Math.Clamp(value, Minimum, Maximum) / Step) * Step;
            snapped = Math.Round(snapped, 4);

            if (snapped == _value)
                return;

            _value = snapped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void Draw(Graphics g)
    {
        DrawText(g, Caption, Theme.Small, Theme.Muted, 0, 2);
        DrawText(g, Format(Value), Theme.SmallBold, Theme.Accent, Width - 2, 2, right: true);

        float left = 8, right = Width - 8, y = 34;
        float fraction = (float)((Value - Minimum) / (Maximum - Minimum));
        float knob = left + ((right - left) * fraction);

        Fill(g, Theme.Line, new RectangleF(left, y - 2, right - left, 4), 2);
        Fill(g, Theme.Accent, new RectangleF(left, y - 2, knob - left, 4), 2);

        using var brush = new SolidBrush(Color.FromArgb(244, 245, 248));
        using var pen = new Pen(Theme.Accent, 2);
        g.FillEllipse(brush, knob - 7, y - 7, 14, 14);
        g.DrawEllipse(pen, knob - 7, y - 7, 14, 14);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _dragging = true;
        SetFromMouse(e.X);
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
            SetFromMouse(e.X);

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        base.OnMouseUp(e);
    }

    private void SetFromMouse(int x)
    {
        double fraction = Math.Clamp((x - 8.0) / Math.Max(1, Width - 16), 0, 1);
        Value = Minimum + ((Maximum - Minimum) * fraction);
    }
}
