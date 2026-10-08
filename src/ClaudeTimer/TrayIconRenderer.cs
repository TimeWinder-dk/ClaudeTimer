using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ClaudeTimer.ViewModels;

namespace ClaudeTimer;

/// <summary>
/// Tegner bakke-ikonet som en forbrugsring med procenttal på en lys, rund
/// baggrund. Baggrunden giver samme kontrast på lys og mørk proceslinje.
/// </summary>
internal static class TrayIconRenderer
{
    private const int Size = 32;

    private static readonly Color Background = Color.FromArgb(0xF7, 0xF7, 0xF9);
    private static readonly Color Track = Color.FromArgb(0xDC, 0xDB, 0xE2);
    private static readonly Color Text = Color.FromArgb(0x19, 0x19, 0x1B);
    private static readonly Color Good = Color.FromArgb(0x1A, 0x9A, 0x4B);
    private static readonly Color Warning = Color.FromArgb(0xE0, 0x96, 0x00);
    private static readonly Color Critical = Color.FromArgb(0xD9, 0x3A, 0x1E);

    public static Icon Render(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        var level = UsageLevels.For(percent) switch
        {
            UsageLevel.Critical => Critical,
            UsageLevel.Warning => Warning,
            _ => Good
        };

        using var bitmap = new Bitmap(Size, Size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);

            using (var background = new SolidBrush(Background))
            {
                graphics.FillEllipse(background, 0, 0, Size - 1, Size - 1);
            }

            var ring = new RectangleF(3f, 3f, Size - 7, Size - 7);
            using (var track = new Pen(Track, 4f))
            {
                graphics.DrawEllipse(track, ring);
            }

            if (percent > 0)
            {
                using var arc = new Pen(level, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                graphics.DrawArc(arc, ring, -90, Math.Max(8, percent * 3.6f));
            }

            var text = percent >= 100 ? "!" : percent.ToString();
            var fontSize = text.Length >= 2 ? 12f : 14f;
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(percent >= 100 ? Critical : Text);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.DrawString(text, font, brush, new RectangleF(0, 0.5f, Size - 1, Size - 1), format);
        }

        // Icon.FromHandle ejer ikke håndtaget; klon ikonet og frigiv det straks.
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
