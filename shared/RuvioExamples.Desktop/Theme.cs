namespace RuvioExamples.Desktop;

internal static class Theme
{
    public static readonly Color Bg = ColorTranslator.FromHtml("#0F1115");
    public static readonly Color Panel = ColorTranslator.FromHtml("#171A21");
    public static readonly Color Panel2 = ColorTranslator.FromHtml("#1E232C");
    public static readonly Color Line = ColorTranslator.FromHtml("#2A303B");
    public static readonly Color Grid = ColorTranslator.FromHtml("#232833");
    public static readonly Color Accent = ColorTranslator.FromHtml("#E0875F");
    public static readonly Color AccentInk = ColorTranslator.FromHtml("#1A0F0A");
    public static readonly Color Text = ColorTranslator.FromHtml("#E6E8EE");
    public static readonly Color Muted = ColorTranslator.FromHtml("#8A93A5");
    public static readonly Color Success = ColorTranslator.FromHtml("#3DDC97");
    public static readonly Color Rejected = ColorTranslator.FromHtml("#FF6B5E");
    public static readonly Color Warn = ColorTranslator.FromHtml("#F5B73B");

    public static readonly Font Body = new("Segoe UI", 9.5f);
    public static readonly Font BodyBold = new("Segoe UI Semibold", 9.5f);
    public static readonly Font Small = new("Segoe UI", 8.5f);
    public static readonly Font SmallBold = new("Segoe UI Semibold", 8.5f);
    public static readonly Font Title = new("Segoe UI Semibold", 13f);
    public static readonly Font Heading = new("Segoe UI Semibold", 19f);
    public static readonly Font Big = new("Segoe UI Semibold", 20f);
    public static readonly Font Mono = new("Consolas", 9.5f);
    public static readonly Font MonoBold = new("Consolas", 9.5f, FontStyle.Bold);
    public static readonly Font MonoSmall = new("Consolas", 8.5f);

    public static Color ForOutcome(RuvioExamples.LoadKit.Outcome outcome) => ForOutcome((byte)outcome);

    public static Color ForOutcome(byte outcome) => outcome switch
    {
        1 => Success,
        2 => Rejected,
        _ => Warn,
    };
}
