using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace RuvioExamples.Desktop.Controls;

/// <summary>Base for every custom-painted control: double buffered, anti-aliased, themed.</summary>
internal abstract class Surface : Control
{
    protected Surface()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);

        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
    }

    protected sealed override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        e.Graphics.Clear(BackColor);
        Draw(e.Graphics);
    }

    protected abstract void Draw(Graphics g);

    protected static void Fill(Graphics g, Color color, RectangleF bounds, float radius = 0)
    {
        if (bounds.Width < 1 || bounds.Height < 1)
            return;

        using var brush = new SolidBrush(color);

        if (radius <= 0 || bounds.Width < radius * 2 || bounds.Height < radius * 2)
        {
            g.FillRectangle(brush, bounds);
            return;
        }

        using var path = Rounded(bounds, radius);
        g.FillPath(brush, path);
    }

    protected static void DrawText(Graphics g, string text, Font font, Color color, float x, float y, bool right = false)
    {
        using var brush = new SolidBrush(color);

        if (right)
        {
            var size = g.MeasureString(text, font);
            g.DrawString(text, font, brush, x - size.Width + 4, y);
        }
        else
        {
            g.DrawString(text, font, brush, x, y);
        }
    }

    internal static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }
}

/// <summary>A panel with a rounded border, the background for every group of controls.</summary>
internal sealed class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = Theme.Panel;
        Padding = new Padding(16);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Bg);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var path = Surface.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 12);
        using var fill = new SolidBrush(Theme.Panel);
        using var pen = new Pen(Theme.Line);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(pen, path);
    }
}

/// <summary>A big number with a small caption.</summary>
internal sealed class Tile : Surface
{
    private string _value = "0";

    public Tile()
    {
        BackColor = Theme.Panel;
        Height = 74;
    }

    [DefaultValue("")]
    public string Caption { get; set; } = string.Empty;

    public Color ValueColor { get; set; } = Theme.Text;

    [Browsable(false)]
    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
                return;

            _value = value;
            Invalidate();
        }
    }

    protected override void Draw(Graphics g)
    {
        using var path = Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 12);
        using var pen = new Pen(Theme.Line);
        g.DrawPath(pen, path);

        DrawText(g, Caption, Theme.SmallBold, Theme.Muted, 14, 10);
        DrawText(g, Value, Theme.Big, ValueColor, 12, 28);
    }
}
