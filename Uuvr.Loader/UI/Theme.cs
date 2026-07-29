namespace Uuvr.Loader.UI;

// Dark palette for the loader, in the spirit of UEVR's frontend.
public static class Theme
{
    public static readonly Color Background = Color.FromArgb(24, 24, 28);
    public static readonly Color Panel = Color.FromArgb(32, 32, 38);
    public static readonly Color PanelLight = Color.FromArgb(42, 42, 50);
    public static readonly Color Border = Color.FromArgb(58, 58, 68);
    public static readonly Color Text = Color.FromArgb(235, 235, 240);
    public static readonly Color TextDim = Color.FromArgb(150, 150, 162);
    public static readonly Color Accent = Color.FromArgb(58, 130, 246);
    public static readonly Color AccentHover = Color.FromArgb(84, 148, 250);
    public static readonly Color Success = Color.FromArgb(74, 190, 120);
    public static readonly Color Warning = Color.FromArgb(226, 178, 66);
    public static readonly Color Danger = Color.FromArgb(224, 96, 96);

    public static Font BaseFont => new("Segoe UI", 9.5f);
    public static Font TitleFont => new("Segoe UI Semibold", 14f);
    public static Font HeaderFont => new("Segoe UI Semibold", 10.5f);
    public static Font MonoFont => new("Consolas", 9f);

    public static Button MakeButton(string text, Color? backColor = null)
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor ?? PanelLight,
            ForeColor = Text,
            Font = BaseFont,
            Height = 34,
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = backColor.HasValue ? AccentHover : Color.FromArgb(54, 54, 64);
        return button;
    }

    public static Label MakeLabel(string text, Color? color = null, Font? font = null)
    {
        return new Label
        {
            Text = text,
            ForeColor = color ?? Text,
            Font = font ?? BaseFont,
            AutoSize = true,
            BackColor = Color.Transparent,
        };
    }
}
