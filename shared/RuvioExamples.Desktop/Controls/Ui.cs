using System.Runtime.InteropServices;

namespace RuvioExamples.Desktop.Controls;

/// <summary>Small factories so the console and the wizard look the same.</summary>
internal static class Ui
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    public static void DarkScrollbars(Control control) => SetWindowTheme(control.Handle, "DarkMode_Explorer", null);

    public static Label Caption(string text) => new()
    {
        Text = text,
        Font = Theme.SmallBold,
        ForeColor = Theme.Muted,
        AutoSize = true,
        Margin = new Padding(0, 14, 0, 6),
    };

    public static Label Muted(string text) => new()
    {
        Text = text,
        Font = Theme.Small,
        ForeColor = Theme.Muted,
        AutoSize = true,
    };

    public static Control Spacer(int height, DockStyle dock = DockStyle.None) => new Panel
    {
        Height = height,
        Width = 10,
        Dock = dock,
        BackColor = Theme.Panel,
        Margin = Padding.Empty,
    };

    public static FlowLayoutPanel FlowPanel(int width) => new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = true,
        Width = width,
        MaximumSize = new Size(width, 0),
        BackColor = Theme.Panel,
        Margin = Padding.Empty,
    };

    public static Button Flat(string text, Color back, Color fore)
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = fore,
            Font = Theme.BodyBold,
            Cursor = Cursors.Hand,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 3, 8, 3),
            UseVisualStyleBackColor = false,
        };

        button.FlatAppearance.BorderColor = Theme.Line;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.1f);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(back, 0.1f);

        return button;
    }

    public static RadioButton PillRadio(string text, object tag, bool isChecked = false)
    {
        var radio = new RadioButton
        {
            Text = text,
            Tag = tag,
            Appearance = Appearance.Button,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Panel2,
            ForeColor = Theme.Text,
            Font = Theme.BodyBold,
            Cursor = Cursors.Hand,
            AutoSize = true,
            Padding = new Padding(8, 3, 8, 3),
            Margin = new Padding(0, 0, 6, 6),
            UseVisualStyleBackColor = false,
            Checked = isChecked,
        };

        radio.FlatAppearance.BorderColor = Theme.Line;
        radio.FlatAppearance.CheckedBackColor = Theme.Accent;
        radio.FlatAppearance.MouseOverBackColor = Theme.Line;
        radio.ForeColor = isChecked ? Theme.AccentInk : Theme.Text;
        radio.CheckedChanged += (_, _) =>
        {
            radio.ForeColor = radio.Checked ? Theme.AccentInk : Theme.Text;
            radio.Invalidate();
        };

        return radio;
    }

    public static CheckBox PillCheck(string text, bool isChecked = false)
    {
        var check = new CheckBox
        {
            Text = text,
            Appearance = Appearance.Button,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Panel2,
            ForeColor = Theme.Text,
            Font = Theme.BodyBold,
            Cursor = Cursors.Hand,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = Padding.Empty,
            Margin = new Padding(0, 0, 6, 6),
            UseVisualStyleBackColor = false,
            Checked = isChecked,
        };

        var measured = TextRenderer.MeasureText(text, Theme.BodyBold);
        check.Size = new Size(measured.Width + 26, measured.Height + 14);

        check.FlatAppearance.BorderColor = Theme.Line;
        check.FlatAppearance.CheckedBackColor = Theme.Accent;
        check.FlatAppearance.MouseOverBackColor = Theme.Line;
        check.ForeColor = isChecked ? Theme.AccentInk : Theme.Text;
        check.CheckedChanged += (_, _) =>
        {
            check.ForeColor = check.Checked ? Theme.AccentInk : Theme.Text;
            check.Invalidate();
        };

        return check;
    }

    public static SliderRow Slider(string caption, double min, double max, double step, double value, Func<double, string> format, int width) => new()
    {
        Caption = caption,
        Minimum = min,
        Maximum = max,
        Step = step,
        Format = format,
        Value = value,
        Width = width,
        BackColor = Theme.Panel,
        Margin = new Padding(0, 4, 0, 0),
    };

    public static Tile NewTile(string caption, Color valueColor) => new()
    {
        Caption = caption,
        ValueColor = valueColor,
        Dock = DockStyle.Fill,
        BackColor = Theme.Panel,
    };
}
