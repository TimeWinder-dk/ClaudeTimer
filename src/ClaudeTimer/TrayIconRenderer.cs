using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ClaudeTimer.ViewModels;

namespace ClaudeTimer;

/// <summary>Tegner bakke-ikonet som en forbrugsring med procenttal i midten.</summary>
internal static class TrayIconRenderer
{
    private const int Size = 32;

    private static readonly Color Good = Color.FromArgb(0x2E, 0xA0, 0x5A);
    private static readonly Color Warning = Color.FromArgb(0xE8, 0xA3, 0x17);
    private static readonly Color Critical = Color.FromArgb(0xE5, 0x48, 0x2E);

    public static Icon Render(int percent, bool lightTaskbar)
    {
        percent = Math.Clamp(percent, 0, 100);
        using var bitmap = new Bitmap(Size, Size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);

            var ring = new RectangleF(2.5f, 2.5f, Size - 5, Size - 5);
            var foreground = lightTaskbar ? Color.FromArgb(0x1A, 0x1A, 0x1A) : Color.White;

            using (var track = new Pen(Color.FromArgb(lightTaskbar ? 50 : 70, foreground), 4f))
            {
                graphics.DrawEllipse(track, ring);
            }

            var color = UsageLevels.For(percent) switch
            {
                UsageLevel.Critical => Critical,
                UsageLevel.Warning => Warning,
                _ => Good
            };

            if (percent > 0)
            {
                using var arc = new Pen(color, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                graphics.DrawArc(arc, ring, -90, Math.Max(8, percent * 3.6f));
            }

            var text = percent >= 100 ? "!" : percent.ToString();
            var fontSize = text.Length >= 2 ? 12.5f : 15f;
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(percent >= 100 ? Critical : foreground);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.DrawString(text, font, brush, new RectangleF(0, 0.5f, Size, Size), format);
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
