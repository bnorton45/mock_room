using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MockRoom.Core.Rooms;

namespace MockRoom.Controls;

/// <summary>
/// Transparent 2D overlay that draws a name badge above each item in the 3D view.
/// Sits above the <see cref="Viewport3DControl"/> in the same Grid so Avalonia
/// composites it on top of the GL framebuffer. Must be non-hit-testable so pointer
/// events fall through to the input overlay below it.
/// </summary>
public sealed class ItemLabels3DControl : Control
{
    public static readonly StyledProperty<MockRoom.Core.Rooms.Room?> RoomProperty =
        AvaloniaProperty.Register<ItemLabels3DControl, MockRoom.Core.Rooms.Room?>(nameof(Room));

    public static readonly StyledProperty<int> RenderVersionProperty =
        AvaloniaProperty.Register<ItemLabels3DControl, int>(nameof(RenderVersion));

    private static readonly IBrush TextBrush = new SolidColorBrush(Colors.White);
    private static readonly IBrush BadgeBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0x20, 0x28, 0x30));
    private static readonly IPen BadgeBorderPen = new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), 1);

    static ItemLabels3DControl()
    {
        AffectsRender<ItemLabels3DControl>(RoomProperty, RenderVersionProperty);
    }

    public MockRoom.Core.Rooms.Room? Room
    {
        get => GetValue(RoomProperty);
        set => SetValue(RoomProperty, value);
    }

    public int RenderVersion
    {
        get => GetValue(RenderVersionProperty);
        set => SetValue(RenderVersionProperty, value);
    }

    /// <summary>
    /// Set in code-behind after the controls are built; used to project world positions
    /// to screen space using the viewport's live camera.
    /// </summary>
    public Viewport3DControl? Viewport { get; set; }

    // FormattedText wraps a Skia layout object; creating one per label per frame during
    // camera drag would produce hundreds of short-lived native allocations per second.
    // Cache by text string — item names and opening kinds change only on RenderVersion bumps.
    private readonly Dictionary<string, FormattedText> _ftCache = new();
    private int _cachedRenderVersion = -1;

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var room = Room;
        var viewport = Viewport;
        if (room is null || viewport is null)
            return;

        // Invalidate the FormattedText cache only when the scene actually changes,
        // not on every camera-drag render.
        var version = RenderVersion;
        if (version != _cachedRenderVersion)
        {
            _ftCache.Clear();
            _cachedRenderVersion = version;
        }

        const double pad = 5;
        const double fontSize = 12;

        foreach (var item in room.Items)
        {
            // Anchor slightly above the item top so the badge doesn't clip into the box.
            var worldPos = new Vector3(
                (float)item.Position.X,
                (float)(item.Height.Meters + 0.15),
                (float)item.Position.Y);

            DrawBadge(context, viewport, worldPos, item.Name, pad, fontSize);
        }

        // Doors and closet doors live in room.Openings, not room.Items.
        var dims = room.Dimensions;
        foreach (var opening in room.Openings)
        {
            if (opening.Kind == OpeningKind.Window)
                continue;

            var label = opening.Kind == OpeningKind.ClosetDoor ? "Closet" : "Door";

            // World-space center of the opening: mid-height, centred on the wall span,
            // inset 0.05 m from the wall plane so it sits just inside the room.
            var cx = (float)opening.OffsetAlongWall.Meters;
            var cy = (float)(opening.SillHeight.Meters + opening.Height.Meters / 2);
            const float inset = 0.05f;
            var worldPos = opening.Wall switch
            {
                WallSide.South => new Vector3(cx, cy, inset),
                WallSide.North => new Vector3(cx, cy, (float)dims.Length.Meters - inset),
                WallSide.West  => new Vector3(inset, cy, cx),
                _              => new Vector3((float)dims.Width.Meters - inset, cy, cx),
            };

            DrawBadge(context, viewport, worldPos, label, pad, fontSize);
        }
    }

    private void DrawBadge(DrawingContext context, Viewport3DControl viewport,
        Vector3 worldPos, string text, double pad, double fontSize)
    {
        var screen = viewport.ProjectToScreen(worldPos);
        if (screen is null) return;

        var pt = screen.Value;
        if (pt.X < -80 || pt.X > Bounds.Width + 80 || pt.Y < -40 || pt.Y > Bounds.Height + 40)
            return;

        if (!_ftCache.TryGetValue(text, out var ft))
        {
            ft = new FormattedText(text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Typeface.Default, fontSize, TextBrush);
            _ftCache[text] = ft;
        }

        var textX = pt.X - ft.Width / 2;
        var textY = pt.Y - ft.Height - pad;
        var badgeRect = new Rect(textX - pad, textY - pad, ft.Width + pad * 2, ft.Height + pad * 2);

        context.DrawRectangle(BadgeBrush, BadgeBorderPen, badgeRect, 3, 3);
        context.DrawText(ft, new Point(textX, textY));
    }
}
