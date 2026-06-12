using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MockRoom.Core.Geometry;
using MockRoom.Core.Items;
using MockRoom.Core.Rooms;
using MockRoom.Core.Spatial;

namespace MockRoom.Controls;

/// <summary>
/// Top-down 2D reference drawing of the room: the floor outline at true
/// proportions, each placed item as a (possibly rotated) colored rectangle, and
/// the doors marked on their walls. Clicking an item selects it.
/// </summary>
public sealed class FloorPlan2DControl : Control
{
    public static readonly StyledProperty<Room?> RoomProperty =
        AvaloniaProperty.Register<FloorPlan2DControl, Room?>(nameof(Room));

    public static readonly StyledProperty<int> RenderVersionProperty =
        AvaloniaProperty.Register<FloorPlan2DControl, int>(nameof(RenderVersion));

    public static readonly StyledProperty<RoomItem?> SelectedItemProperty =
        AvaloniaProperty.Register<FloorPlan2DControl, RoomItem?>(
            nameof(SelectedItem), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<SpaceReport?> SpaceReportProperty =
        AvaloniaProperty.Register<FloorPlan2DControl, SpaceReport?>(nameof(SpaceReport));

    private double _scale = 1;
    private double _offsetX;
    private double _offsetY;
    private bool _dragging;
    private Vec2 _grabOffset;

    /// <summary>Raised while the user drags an item; carries the item and its desired new center (world meters).</summary>
    public event Action<RoomItem, Vec2>? ItemDragged;

    private static readonly IBrush FreeFloorBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x2F, 0x6E, 0xB0));

    static FloorPlan2DControl()
    {
        AffectsRender<FloorPlan2DControl>(
            RoomProperty, RenderVersionProperty, SelectedItemProperty, SpaceReportProperty);
    }

    public Room? Room
    {
        get => GetValue(RoomProperty);
        set => SetValue(RoomProperty, value);
    }

    public int RenderVersion
    {
        get => GetValue(RenderVersionProperty);
        set => SetValue(RenderVersionProperty, value);
    }

    public RoomItem? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>Latest space report; its grid drives the blue free-floor overlay.</summary>
    public SpaceReport? SpaceReport
    {
        get => GetValue(SpaceReportProperty);
        set => SetValue(SpaceReportProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var room = Room;
        if (room is null)
            return;

        var roomW = room.Dimensions.Width.Meters;
        var roomL = room.Dimensions.Length.Meters;
        if (roomW <= 0 || roomL <= 0)
            return;

        const double pad = 24;
        var availW = Bounds.Width - 2 * pad;
        var availH = Bounds.Height - 2 * pad;
        if (availW <= 0 || availH <= 0)
            return;

        _scale = System.Math.Min(availW / roomW, availH / roomL);
        _offsetX = pad + (availW - roomW * _scale) / 2;
        _offsetY = pad + (availH - roomL * _scale) / 2;

        // Floor.
        var floor = new Rect(ToPx(new Vec2(0, 0)), ToPx(new Vec2(roomW, roomL)));
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x36)), floor);

        DrawFreeFloor(context, roomW, roomL);

        context.DrawRectangle(null, new Pen(Brushes.White, 2), floor);

        DrawDoors(context, room);

        foreach (var item in room.Items)
            DrawItem(context, item, ReferenceEquals(item, SelectedItem));

        DrawDimensionLabels(context, room, floor);
    }

    private void DrawFreeFloor(DrawingContext context, double roomW, double roomL)
    {
        var grid = SpaceReport?.Grid;
        if (grid is null)
            return;

        var cell = grid.CellSize;
        foreach (var (row, colStart, colEnd) in grid.FreeRuns())
        {
            var x0 = colStart * cell;
            var x1 = System.Math.Min(colEnd * cell, roomW);
            var y0 = row * cell;
            var y1 = System.Math.Min((row + 1) * cell, roomL);
            context.FillRectangle(FreeFloorBrush, new Rect(ToPx(new Vec2(x0, y0)), ToPx(new Vec2(x1, y1))));
        }
    }

    private void DrawItem(DrawingContext context, RoomItem item, bool selected)
    {
        var (p0, p1, p2, p3) = item.Footprint.Corners();
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(ToPx(p0), isFilled: true);
            ctx.LineTo(ToPx(p1));
            ctx.LineTo(ToPx(p2));
            ctx.LineTo(ToPx(p3));
            ctx.EndFigure(isClosed: true);
        }

        var fill = new SolidColorBrush(Color.Parse(item.ColorHex), 0.85);
        var pen = selected
            ? new Pen(Brushes.White, 2.5)
            : new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), 1);
        context.DrawGeometry(fill, pen, geometry);

        DrawCentered(context, item.Name, ToPx(item.Position), 11, Brushes.White);
    }

    private void DrawDoors(DrawingContext context, Room room)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xE6, 0xB8, 0x4C)), 4);
        foreach (var door in room.Doors)
        {
            var (a, b) = DoorEndpoints(door, room.Dimensions);
            context.DrawLine(pen, ToPx(a), ToPx(b));
        }
    }

    private static (Vec2 A, Vec2 B) DoorEndpoints(Door door, RoomDimensions dims)
    {
        var off = door.OffsetAlongWall.Meters;
        var half = door.Width.Meters / 2;
        var w = dims.Width.Meters;
        var l = dims.Length.Meters;
        return door.Wall switch
        {
            WallSide.South => (new Vec2(off - half, 0), new Vec2(off + half, 0)),
            WallSide.North => (new Vec2(off - half, l), new Vec2(off + half, l)),
            WallSide.West => (new Vec2(0, off - half), new Vec2(0, off + half)),
            _ => (new Vec2(w, off - half), new Vec2(w, off + half)),
        };
    }

    private void DrawDimensionLabels(DrawingContext context, Room room, Rect floor)
    {
        var unit = room.PreferredUnits == Core.Units.UnitSystem.Imperial ? "ft" : "m";
        var w = room.PreferredUnits == Core.Units.UnitSystem.Imperial
            ? room.Dimensions.Width.Feet : room.Dimensions.Width.Meters;
        var l = room.PreferredUnits == Core.Units.UnitSystem.Imperial
            ? room.Dimensions.Length.Feet : room.Dimensions.Length.Meters;

        DrawCentered(context, $"{w:0.##} {unit}",
            new Point(floor.Center.X, floor.Top - 12), 12, Brushes.LightGray);
        DrawCentered(context, $"{l:0.##} {unit}",
            new Point(floor.Left - 14, floor.Center.Y), 12, Brushes.LightGray);
    }

    private static void DrawCentered(DrawingContext context, string text, Point center, double size, IBrush brush)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, size, brush);
        context.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
    }

    private Point ToPx(Vec2 world) => new(_offsetX + world.X * _scale, _offsetY + world.Y * _scale);

    private Vec2 ToWorld(Point px) => new((px.X - _offsetX) / _scale, (px.Y - _offsetY) / _scale);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var room = Room;
        if (room is null)
            return;

        var world = ToWorld(e.GetPosition(this));
        // Topmost (last drawn) item wins.
        for (var i = room.Items.Count - 1; i >= 0; i--)
        {
            var item = room.Items[i];
            if (item.Footprint.Contains(world))
            {
                SelectedItem = item;
                _dragging = true;
                _grabOffset = world - item.Position; // keep the grab point under the cursor
                e.Pointer.Capture(this);
                return;
            }
        }
        SelectedItem = null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || SelectedItem is null)
            return;

        var world = ToWorld(e.GetPosition(this));
        ItemDragged?.Invoke(SelectedItem, world - _grabOffset);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging)
        {
            _dragging = false;
            e.Pointer.Capture(null);
        }
    }
}
