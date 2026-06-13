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
        Width = 300;
        Height = 340;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _picker = new ColorView
        {
            ColorSpectrumShape = ColorSpectrumShape.Ring,
            IsAlphaEnabled = false,
            IsAlphaVisible = false,
            IsColorPaletteVisible = false,
            SelectedIndex = 0,
            Margin = new Thickness(8),
        };

        _picker.ColorChanged += (_, e) => ColorPicked?.Invoke(e.NewColor);
        Content = _picker;
    }

    public void SetColor(Color color) => _picker.Color = color;
}
