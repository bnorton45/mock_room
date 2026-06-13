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
    private double _roomL;
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
        _roomL = roomL; // cached so ToPx/ToWorld can flip Y (North at the top, globe convention)

        // Floor.
        var floor = new Rect(ToPx(new Vec2(0, 0)), ToPx(new Vec2(roomW, roomL)));
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x36)), floor);

        DrawFreeFloor(context, roomW, roomL);

        context.DrawRectangle(null, new Pen(Brushes.White, 2), floor);

        DrawOpenings(context, room);

        foreach (var item in room.Items)
            DrawItem(context, item, ReferenceEquals(item, SelectedItem));

        DrawDimensionLabels(context, room, floor);
        DrawCompass(context);
    }

    /// <summary>Draws a small fixed reference compass (North up) in the top-left corner.</summary>
    private void DrawCompass(DrawingContext context)
    {
        const double r = 20;
        var center = new Point(34, 34);
        var ring = new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0xC9, 0xD1, 0xD9)), 1.5);
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x66, 0x20, 0x28, 0x30)), ring, center, r, r);

        // North needle: a red triangle pointing up.
        var needle = new StreamGeometry();
        using (var ctx = needle.Open())
        {
            ctx.BeginFigure(new Point(center.X, center.Y - r + 3), isFilled: true);
            ctx.LineTo(new Point(center.X - 5, center.Y + 2));
            ctx.LineTo(new Point(center.X + 5, center.Y + 2));
            ctx.EndFigure(isClosed: true);
        }
        context.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x6C)), null, needle);

        var labels = new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xD9));
        DrawCentered(context, "N", new Point(center.X, center.Y - r - 7), 11, labels);
        DrawCentered(context, "S", new Point(center.X, center.Y + r + 7), 10, labels);
        DrawCentered(context, "E", new Point(center.X + r + 7, center.Y), 10, labels);
        DrawCentered(context, "W", new Point(center.X - r - 7, center.Y), 10, labels);
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

    private static readonly Color DoorColor = Color.FromRgb(0xE6, 0xB8, 0x4C);
    private static readonly Color WindowColor = Color.FromRgb(0x5A, 0xB0, 0xFF);

    private void DrawOpenings(DrawingContext context, Room room)
    {
        var dims = room.Dimensions;
        var doorPen = new Pen(new SolidColorBrush(DoorColor), 4);
        var arcPen = new Pen(new SolidColorBrush(DoorColor, 0.7), 1.5);
        var leafPen = new Pen(new SolidColorBrush(DoorColor), 5);
        var windowPen = new Pen(new SolidColorBrush(WindowColor), 4);

        foreach (var opening in room.Openings)
        {
            var (a, b) = OpeningEndpoints(opening, dims);
            if (opening.Kind == OpeningKind.Window)
            {
                // A window: a band across the wall, no swing.
                context.DrawLine(windowPen, ToPx(a), ToPx(b));
                continue;
            }

            // A door or closet door: the wall gap, its swing arc(s), and the open leaf at 90°.
            context.DrawLine(doorPen, ToPx(a), ToPx(b));
            foreach (var arc in opening.FloorRegions(dims))
            {
                DrawSwingArc(context, arcPen, arc);
                context.DrawLine(leafPen, ToPx(arc.Hinge), ToPx(arc.Hinge + arc.DirB * arc.Radius));
            }
        }
    }

    /// <summary>Draws the quarter-circle a door leaf sweeps as a sampled polyline.</summary>
    private void DrawSwingArc(DrawingContext context, Pen pen, Core.Geometry.SwingArc arc)
    {
        const int segments = 12;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            // Sweep from the closed leaf (DirA) toward the open leaf (DirB) at the radius.
            ctx.BeginFigure(ToPx(arc.Hinge), isFilled: false);
            for (var i = 0; i <= segments; i++)
            {
                var t = (double)i / segments * (Math.PI / 2);
                var dir = arc.DirA * Math.Cos(t) + arc.DirB * Math.Sin(t);
                ctx.LineTo(ToPx(arc.Hinge + dir * arc.Radius));
            }
            ctx.EndFigure(isClosed: true);
        }
        context.DrawGeometry(null, pen, geometry);
    }

    private static (Vec2 A, Vec2 B) OpeningEndpoints(WallOpening opening, RoomDimensions dims)
    {
        var off = opening.OffsetAlongWall.Meters;
        var half = opening.OuterWidth.Meters / 2;
        var w = dims.Width.Meters;
        var l = dims.Length.Meters;
        return opening.Wall switch
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

    // Y is flipped so North (the far y = length wall) draws at the top of the plan.
    private Point ToPx(Vec2 world) => new(_offsetX + world.X * _scale, _offsetY + (_roomL - world.Y) * _scale);

    private Vec2 ToWorld(Point px) => new((px.X - _offsetX) / _scale, _roomL - (px.Y - _offsetY) / _scale);

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
