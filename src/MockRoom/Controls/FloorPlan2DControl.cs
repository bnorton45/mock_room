using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MockRoom.Core.Geometry;
using MockRoom.Core.Items;
using MockRoom.Core.Rendering;
using MockRoom.Core.Rooms;

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

    public static readonly StyledProperty<PaintTarget?> SelectedTargetProperty =
        AvaloniaProperty.Register<FloorPlan2DControl, PaintTarget?>(
            nameof(SelectedTarget), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private double _scale = 1;
    private double _offsetX;
    private double _offsetY;
    private double _roomL;
    private bool _dragging;
    private Vec2 _grabOffset;

    /// <summary>Raised while the user drags an item; carries the item and its desired new center (world meters).</summary>
    public event Action<RoomItem, Vec2>? ItemDragged;

    private static readonly IPen InnerEdgePen = new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), 1);

    static FloorPlan2DControl()
    {
        AffectsRender<FloorPlan2DControl>(
            RoomProperty, RenderVersionProperty, SelectedTargetProperty);
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

    public PaintTarget? SelectedTarget
    {
        get => GetValue(SelectedTargetProperty);
        set => SetValue(SelectedTargetProperty, value);
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

        // 0.15 m wall thickness in world space, minimum 6 px so thin rooms still show walls.
        var wallPx = System.Math.Max(6.0, 0.15 * _scale);
        var outer = floor.Inflate(wallPx);

        var selectedWall = (SelectedTarget as WallPaintTarget)?.Side;

        // Four wall strips — each gets its own colour, highlighted when selected.
        // North/South strips span full outer width; East/West fill only the floor height.
        void DrawWallStrip(Rect rect, WallSide side)
        {
            var hex = room.Surfaces.WallColorFor(side);
            IBrush brush = selectedWall == side
                ? new SolidColorBrush(Brighten(ParseHex(hex)))
                : new SolidColorBrush(ParseHex(hex));
            context.FillRectangle(brush, rect);
        }

        DrawWallStrip(new Rect(outer.X, outer.Y, outer.Width, wallPx), WallSide.North);
        DrawWallStrip(new Rect(outer.X, floor.Y + floor.Height, outer.Width, wallPx), WallSide.South);
        DrawWallStrip(new Rect(outer.X, floor.Y, wallPx, floor.Height), WallSide.West);
        DrawWallStrip(new Rect(floor.X + floor.Width, floor.Y, wallPx, floor.Height), WallSide.East);

        // Floor interior — always drawn at the actual floor color.
        context.FillRectangle(new SolidColorBrush(ParseHex(room.Surfaces.FloorColorHex)), floor);

        // Selection outline when the floor is the active paint target.
        if (SelectedTarget is FloorPaintTarget)
            context.DrawRectangle(null, SelectedItemPen, floor);

        // Thin line marking the wall-floor inner boundary.
        context.DrawRectangle(null, InnerEdgePen, floor);

        DrawOpenings(context, room);

        var selectedItem = (SelectedTarget as ItemPaintTarget)?.Item;
        foreach (var item in room.Items)
            DrawItem(context, item, ReferenceEquals(item, selectedItem));

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

    private void DrawItem(DrawingContext context, RoomItem item, bool selected)
    {
        var pen = selected ? SelectedItemPen : UnselectedItemPen;

        if (item is FurnitureItem furniture)
            DrawFurnitureItem(context, furniture, pen);
        else
            DrawItemPolygon(context, item.Footprint.Corners(), ParseHex(item.ColorHex), pen);

        DrawCentered(context, item.Name, ToPx(item.Position), 11, Brushes.White);
    }

    private void DrawFurnitureItem(DrawingContext context, FurnitureItem item, IPen pen)
    {
        var pos = item.Position;
        var rot = item.Rotation;
        var cos = System.Math.Cos(rot);
        var sin = System.Math.Sin(rot);

        var sx = item.NaturalWidth.Meters > 0 ? item.Width.Meters / item.NaturalWidth.Meters : 1.0;
        var sy = item.NaturalDepth.Meters > 0 ? item.Depth.Meters / item.NaturalDepth.Meters : 1.0;

        // Sort parts bottom-up so higher parts are drawn on top (e.g. tabletop over legs in 2D).
        var sortedParts = item.Parts
            .OrderBy(p => p.BottomY)
            .ThenBy(p => p.Height);

        foreach (var part in sortedParts)
        {
            var lx = part.LocalX * sx;
            var ly = part.LocalY * sy;
            var worldCenter = new Vec2(
                pos.X + lx * cos - ly * sin,
                pos.Y + lx * sin + ly * cos);
            var footprint = new FootprintRect(worldCenter, part.Width * sx, part.Depth * sy, rot);
            var color = part.ColorHex is not null ? ParseHex(part.ColorHex) : ParseHex(item.ColorHex);
            DrawItemPolygon(context, footprint.Corners(), color, pen);
        }
    }

    private void DrawItemPolygon(DrawingContext context, (Vec2, Vec2, Vec2, Vec2) corners, Color color, IPen pen)
    {
        var (p0, p1, p2, p3) = corners;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(ToPx(p0), isFilled: true);
            ctx.LineTo(ToPx(p1));
            ctx.LineTo(ToPx(p2));
            ctx.LineTo(ToPx(p3));
            ctx.EndFigure(isClosed: true);
        }
        context.DrawGeometry(new SolidColorBrush(color, 0.85), pen, geometry);
    }

    private static readonly Color DoorColor = Color.FromRgb(0xE6, 0xB8, 0x4C);
    private static readonly Color WindowColor = Color.FromRgb(0x5A, 0xB0, 0xFF);

    // Opening pens (declared after DoorColor/WindowColor so static init order is correct).
    private static readonly IPen DoorPen = new Pen(new SolidColorBrush(DoorColor), 4);
    private static readonly IPen ArcPen = new Pen(new SolidColorBrush(DoorColor, 0.7), 1.5);
    private static readonly IPen LeafPen = new Pen(new SolidColorBrush(DoorColor), 5);
    private static readonly IPen WindowPen = new Pen(new SolidColorBrush(WindowColor), 4);

    // Item pens.
    private static readonly IPen SelectedItemPen = new Pen(Brushes.White, 2.5);
    private static readonly IPen UnselectedItemPen = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), 1);

    // Compass resources — the needle is a fixed shape so we build it once.
    private static readonly IPen CompassRingPen = new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0xC9, 0xD1, 0xD9)), 1.5);
    private static readonly IBrush CompassBgBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x20, 0x28, 0x30));
    private static readonly IBrush CompassNeedleBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x6C));
    private static readonly IBrush CompassLabelBrush = new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xD9));
    private static readonly Geometry CompassNeedle = BuildCompassNeedle();

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

    private static Color ParseHex(string hex)
    {
        if (Color.TryParse(hex, out var c))
            return c;
        return Color.FromRgb(0x9A, 0xA0, 0xA6);
    }

    private static Color Brighten(Color c)
    {
        static byte Mix(byte v) => (byte)(v + (255 - v) * 55 / 100);
        return Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var room = Room;
        if (room is null)
            return;

        var world = ToWorld(e.GetPosition(this));

        // 1. Items: topmost (last drawn) wins.
        for (var i = room.Items.Count - 1; i >= 0; i--)
        {
            var item = room.Items[i];
            if (item.Footprint.Contains(world))
            {
                SelectedTarget = new ItemPaintTarget(item);
                _dragging = true;
                _grabOffset = world - item.Position;
                e.Pointer.Capture(this);
                return;
            }
        }

        // 2. Walls: click within ~0.25 m world-space of any wall edge.
        var tol = System.Math.Max(0.2, 20.0 / _scale);
        var w = room.Dimensions.Width.Meters;
        var l = room.Dimensions.Length.Meters;

        if (world.Y >= l - tol) { SelectedTarget = new WallPaintTarget(WallSide.North); return; }
        if (world.Y <= tol) { SelectedTarget = new WallPaintTarget(WallSide.South); return; }
        if (world.X <= tol) { SelectedTarget = new WallPaintTarget(WallSide.West); return; }
        if (world.X >= w - tol) { SelectedTarget = new WallPaintTarget(WallSide.East); return; }

        // 3. Floor: anywhere inside the room bounds.
        if (world.X >= 0 && world.X <= w && world.Y >= 0 && world.Y <= l)
        {
            SelectedTarget = new FloorPaintTarget();
            return;
        }

        SelectedTarget = null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || SelectedTarget is not ItemPaintTarget { Item: var dragItem })
            return;

        var world = ToWorld(e.GetPosition(this));
        ItemDragged?.Invoke(dragItem, world - _grabOffset);
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
