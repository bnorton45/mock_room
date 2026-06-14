using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MockRoom.Controls;

/// <summary>
/// Small floating window hosting a <see cref="ColorView"/> ring wheel.
/// Fires <see cref="ColorPicked"/> on every change so the caller can apply
/// the color immediately without any binding plumbing.
/// </summary>
public sealed class ColorPickerWindow : Window
{
    private readonly ColorView _picker;

    /// <summary>Raised on every color change while the user drags the wheel.</summary>
    public event Action<Color>? ColorPicked;

    public ColorPickerWindow()
    {
        Title = "Color";
        Width = 380;
        Height = 440;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _picker = new ColorView
        {
            ColorSpectrumShape    = ColorSpectrumShape.Ring,
            IsAlphaEnabled        = false,
            IsAlphaVisible        = false,
            IsColorPaletteVisible = false,
            IsHexInputVisible     = true,
            ColorModel            = ColorModel.Rgba,
            SelectedIndex         = 0,
            Margin                = new Thickness(12),
        };

        // Avalonia's ColorChanged fires with byte-level Color derived from the ring's HsvColor.
        // The ring operates in floating-point HSV, and Avalonia's internal HSV→RGB conversion
        // truncates (byte)(channel * 255) rather than rounding. For colours very close to a
        // pure hue boundary (e.g. H≈59.9°, S=1, V=1 → G≈0.9997) this produces 254 instead of
        // 255. We re-derive the colour from HsvColor with explicit Math.Round to fix this.
        // When the change originates from the hex input or RGBA sliders the round-trip through
        // HsvColor is lossless for the common case, so accuracy is preserved.
        _picker.ColorChanged += (_, _) => ColorPicked?.Invoke(HsvToColor(_picker.HsvColor));
        Content = _picker;
    }

    public void SetColor(Color color) => _picker.Color = color;

    private static Color HsvToColor(HsvColor hsv)
    {
        var h = ((hsv.H % 360) + 360) % 360;
        var s = hsv.S;
        var v = hsv.V;

        double r, g, b;
        if (s == 0)
        {
            r = g = b = v;
        }
        else
        {
            var sector = h / 60.0;
            var i = (int)Math.Floor(sector);
            var f = sector - i;
            var p = v * (1 - s);
            var q = v * (1 - s * f);
            var t = v * (1 - s * (1 - f));
            (r, g, b) = i switch
            {
                0 => (v, t, p),
                1 => (q, v, p),
                2 => (p, v, t),
                3 => (p, q, v),
                4 => (t, p, v),
                _ => (v, p, q),
            };
        }

        return Color.FromRgb(
            (byte)Math.Round(r * 255),
            (byte)Math.Round(g * 255),
            (byte)Math.Round(b * 255));
    }
}
