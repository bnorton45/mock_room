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

    // All brushes and pens that use fixed colours are static so they are allocated once,
    // not on every Render call.
    private static readonly IBrush WallBrush    = new SolidColorBrush(Color.FromRgb(0x72, 0x7A, 0x84));
    private static readonly IBrush FloorBrush   = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x36));
    private static readonly IPen   InnerEdgePen = new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), 1);

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

        // Build the floor rect from raw screen-space values rather than from two ToPx calls:
        // Rect(Point,Point) in Avalonia 11 does not normalise, so passing a Y-flipped pair
        // produces a negative height and the rect is silently skipped by every draw call.
        var floor = new Rect(_offsetX, _offsetY, roomW * _scale, roomL * _scale);

        // Walls: draw a filled outer shell so the wall mass is clearly visible.
        // 0.15 m wall thickness in world space, minimum 6 px so thin rooms still show walls.
        var wallPx = System.Math.Max(6.0, 0.15 * _scale);
        var outer = floor.Inflate(wallPx);
        context.FillRectangle(WallBrush, outer);

        // Floor interior.
        context.FillRectangle(FloorBrush, floor);

        DrawFreeFloor(context, roomW, roomL);

        // Thin line marking the wall-floor inner boundary.
        context.DrawRectangle(null, InnerEdgePen, floor);

        DrawOpenings(context, room);

        foreach (var item in room.Items)
            DrawItem(context, item, ReferenceEquals(item, SelectedItem));

        DrawDimensionLabels(context, room, floor);
        DrawCompass(context);
    }

    /// <summary>Draws a small fixed reference compass (North up) in the top-left corner.</summary>
    private static void DrawCompass(DrawingContext context)
    {
        const double r = 20;
        var center = new Point(34, 34);
        context.DrawEllipse(CompassBgBrush, CompassRingPen, center, r, r);
        context.DrawGeometry(CompassNeedleBrush, null, CompassNeedle);
        DrawCentered(context, "N", new Point(center.X, center.Y - r - 7), 11, CompassLabelBrush);
        DrawCentered(context, "S", new Point(center.X, center.Y + r + 7), 10, CompassLabelBrush);
        DrawCentered(context, "E", new Point(center.X + r + 7, center.Y), 10, CompassLabelBrush);
        DrawCentered(context, "W", new Point(center.X - r - 7, center.Y), 10, CompassLabelBrush);
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
            // Use explicit Rect construction: Rect(Point,Point) in Avalonia 11 does not
            // normalise coordinates, so the Y-flipped pair would produce negative height.
            var cellRect = new Rect(
                _offsetX + x0 * _scale,
                _offsetY + (roomL - y1) * _scale,   // y1 is the world-top → screen-top
                (x1 - x0) * _scale,
                (y1 - y0) * _scale);
            context.FillRectangle(FreeFloorBrush, cellRect);
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
        context.DrawGeometry(fill, selected ? SelectedItemPen : UnselectedItemPen, geometry);

        DrawCentered(context, item.Name, ToPx(item.Position), 11, Brushes.White);
    }

    private static readonly Color DoorColor = Color.FromRgb(0xE6, 0xB8, 0x4C);
    private static readonly Color WindowColor = Color.FromRgb(0x5A, 0xB0, 0xFF);

    // Opening pens (declared after DoorColor/WindowColor so static init order is correct).
    private static readonly IPen DoorPen   = new Pen(new SolidColorBrush(DoorColor), 4);
    private static readonly IPen ArcPen    = new Pen(new SolidColorBrush(DoorColor, 0.7), 1.5);
    private static readonly IPen LeafPen   = new Pen(new SolidColorBrush(DoorColor), 5);
    private static readonly IPen WindowPen = new Pen(new SolidColorBrush(WindowColor), 4);

    // Item pens.
    private static readonly IPen SelectedItemPen   = new Pen(Brushes.White, 2.5);
    private static readonly IPen UnselectedItemPen = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), 1);

    // Compass resources — the needle is a fixed shape so we build it once.
    private static readonly IPen    CompassRingPen    = new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0xC9, 0xD1, 0xD9)), 1.5);
    private static readonly IBrush  CompassBgBrush    = new SolidColorBrush(Color.FromArgb(0x66, 0x20, 0x28, 0x30));
    private static readonly IBrush  CompassNeedleBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x6C));
    private static readonly IBrush  CompassLabelBrush = new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xD9));
    private static readonly Geometry CompassNeedle    = BuildCompassNeedle();

    private static Geometry BuildCompassNeedle()
    {
        const double r = 20;
        var cx = 34.0;
        var cy = 34.0;
        var geo = new StreamGeometry();
        using var ctx = geo.Open();
        ctx.BeginFigure(new Point(cx, cy - r + 3), isFilled: true);
        ctx.LineTo(new Point(cx - 5, cy + 2));
        ctx.LineTo(new Point(cx + 5, cy + 2));
        ctx.EndFigure(isClosed: true);
        return geo;
    }

    private void DrawOpenings(DrawingContext context, Room room)
    {
        var dims = room.Dimensions;

        foreach (var opening in room.Openings)
        {
            var (a, b) = OpeningEndpoints(opening, dims);
            if (opening.Kind == OpeningKind.Window)
            {
                context.DrawLine(WindowPen, ToPx(a), ToPx(b));
                continue;
            }

            context.DrawLine(DoorPen, ToPx(a), ToPx(b));
            foreach (var arc in opening.FloorRegions(dims))
            {
                DrawSwingArc(context, ArcPen, arc);
                context.DrawLine(LeafPen, ToPx(arc.Hinge), ToPx(arc.Hinge + arc.DirB * arc.Radius));
            }
        }
    }

    /// <summary>Draws the quarter-circle a door leaf sweeps as a sampled polyline.</summary>
    private void DrawSwingArc(DrawingContext context, IPen pen, Core.Geometry.SwingArc arc)
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
