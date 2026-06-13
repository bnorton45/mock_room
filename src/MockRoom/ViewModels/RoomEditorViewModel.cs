using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using MockRoom.Core.Geometry;
using MockRoom.Core.Items;
using MockRoom.Core.Persistence;
using MockRoom.Core.Rooms;
using MockRoom.Core.Spatial;
using MockRoom.Core.Units;

namespace MockRoom.ViewModels;

/// <summary>
/// Drives the room editor: room dimensions, unit system, the furniture catalog,
/// placed items, and the live space report. Recomputes usable area whenever the
/// room changes and bumps <see cref="RenderVersion"/> so the floor-plan view redraws.
/// </summary>
public sealed class RoomEditorViewModel : ViewModelBase
{
    private readonly ISpaceCalculator _calculator;
    private readonly IUnitFormatter _formatter;
    private readonly IRoomRepository _repository;

    /// <summary>Default footprint and height for a freshly added box, in meters (a basic 1 m editable cube).</summary>
    private const double DefaultBoxMeters = 1.0;
    private const string DefaultBoxColor = "#6E7B8B";

    /// <summary>Default sizes for freshly added openings, in meters.</summary>
    private const double DefaultDoorWidthMeters = 0.9;
    private const double DefaultDoorHeightMeters = 2.0;
    private const double DefaultClosetWidthMeters = 1.5;
    private const double DefaultWindowWidthMeters = 1.2;
    private const double DefaultWindowHeightMeters = 1.2;
    private const double DefaultWindowSillMeters = 0.9;
    private const double DefaultWindowFrameMeters = 0.05;

    private UnitSystem _unitSystem = UnitSystem.Metric;
    private string _newItemName = "";
    private string _widthText = "";
    private string _lengthText = "";
    private string _heightText = "";
    private string _totalAreaText = "";
    private string _usedAreaText = "";
    private string _freeAreaText = "";
    private double _freeFraction = 1.0;
    private string? _dimensionError;
    private RoomItem? _selectedItem;
    private int _renderVersion;
    private int _placementCounter;
    private bool _snapEnabled = true;
    private string _itemWidthText = "";
    private string _itemDepthText = "";
    private string _itemHeightText = "";
    private string _itemRotationText = "";
    private string? _itemError;
    private WallOpening? _selectedOpening;
    private WallSide _openingWall = WallSide.South;
    private HingeSide _openingHinge = HingeSide.Start;
    private string _openingOffsetText = "";
    private string _openingWidthText = "";
    private string _openingHeightText = "";
    private string _openingSillText = "";
    private string _frameTopText = "";
    private string _frameBottomText = "";
    private string _frameLeftText = "";
    private string _frameRightText = "";
    private string? _openingError;
    private bool _is3D;
    private Core.Rendering.CameraMode _cameraMode = Core.Rendering.CameraMode.FirstPerson;
    private double _eyeHeightMeters = 1.6;
    private double _eyeHeightMaxMeters = 2.5;
    private SpaceReport? _lastReport;

    public RoomEditorViewModel()
        : this(new OccupancyGridSpaceCalculator(), UnitFormatter.Instance)
    {
    }

    public RoomEditorViewModel(ISpaceCalculator calculator, IUnitFormatter formatter,
        IRoomRepository? repository = null)
    {
        _calculator = calculator;
        _formatter = formatter;
        _repository = repository ?? new JsonRoomRepository();

        Room = new Room(RoomDimensions.FromMeters(5, 4, 2.5), UnitSystem.Metric);

        AddBoxCommand = new RelayCommand(_ => AddBox());
        RemoveSelectedCommand = new RelayCommand(_ => RemoveSelected(), _ => SelectedItem is not null);
        SetMetricCommand = new RelayCommand(_ => SetUnitSystem(UnitSystem.Metric));
        SetImperialCommand = new RelayCommand(_ => SetUnitSystem(UnitSystem.Imperial));
        ApplyDimensionsCommand = new RelayCommand(_ => ApplyDimensions());
        ApplyItemCommand = new RelayCommand(_ => ApplyItem(), _ => SelectedItem is not null);
        SetFirstPersonCommand = new RelayCommand(_ => CameraMode = Core.Rendering.CameraMode.FirstPerson);
        SetOrbitCommand = new RelayCommand(_ => CameraMode = Core.Rendering.CameraMode.Orbit);
        AddDoorCommand = new RelayCommand(_ => AddOpening(OpeningKind.Door));
        AddClosetDoorCommand = new RelayCommand(_ => AddOpening(OpeningKind.ClosetDoor));
        AddWindowCommand = new RelayCommand(_ => AddOpening(OpeningKind.Window));
        RemoveOpeningCommand = new RelayCommand(_ => RemoveSelectedOpening(), _ => SelectedOpening is not null);
        ApplyOpeningCommand = new RelayCommand(_ => ApplyOpening(), _ => SelectedOpening is not null);

        _eyeHeightMaxMeters = Room.Dimensions.Height.Meters;
        RefreshDimensionTexts();
        Recompute();
    }

    public Room Room { get; }

    /// <summary>Latest space report; bound by the views to draw the blue free-floor overlay.</summary>
    public SpaceReport? LastReport { get => _lastReport; private set => SetField(ref _lastReport, value); }
    public ObservableCollection<RoomItem> Items { get; } = [];

    public RelayCommand AddBoxCommand { get; }
    public RelayCommand RemoveSelectedCommand { get; }
    public RelayCommand SetMetricCommand { get; }
    public RelayCommand SetImperialCommand { get; }
    public RelayCommand ApplyDimensionsCommand { get; }
    public RelayCommand ApplyItemCommand { get; }
    public RelayCommand SetFirstPersonCommand { get; }
    public RelayCommand SetOrbitCommand { get; }
    public RelayCommand AddDoorCommand { get; }
    public RelayCommand AddClosetDoorCommand { get; }
    public RelayCommand AddWindowCommand { get; }
    public RelayCommand RemoveOpeningCommand { get; }
    public RelayCommand ApplyOpeningCommand { get; }

    /// <summary>Wall openings (doors, closet doors, windows) shown in the openings list.</summary>
    public ObservableCollection<WallOpening> Openings { get; } = [];

    /// <summary>Choices for the opening editor's wall and hinge combo boxes.</summary>
    public WallSide[] WallSides { get; } = Enum.GetValues<WallSide>();
    public HingeSide[] HingeSides { get; } = Enum.GetValues<HingeSide>();

    /// <summary>True when the center pane shows the 3D viewport instead of the 2D plan.</summary>
    public bool Is3D
    {
        get => _is3D;
        set
        {
            if (SetField(ref _is3D, value))
                OnPropertyChanged(nameof(Is2D));
        }
    }

    /// <summary>Inverse of <see cref="Is3D"/>; lets the view-toggle radio buttons share one piece of state.</summary>
    public bool Is2D
    {
        get => !_is3D;
        set
        {
            if (value)
                Is3D = false;
        }
    }

    /// <summary>Active 3D camera mode; bound to the viewport control.</summary>
    public Core.Rendering.CameraMode CameraMode
    {
        get => _cameraMode;
        set
        {
            if (SetField(ref _cameraMode, value))
            {
                OnPropertyChanged(nameof(IsFirstPerson));
                OnPropertyChanged(nameof(IsOrbit));
            }
        }
    }

    public bool IsFirstPerson => _cameraMode == Core.Rendering.CameraMode.FirstPerson;
    public bool IsOrbit => _cameraMode == Core.Rendering.CameraMode.Orbit;

    /// <summary>First-person eye height in meters; bound to the viewport and the height slider.</summary>
    public double EyeHeightMeters
    {
        get => _eyeHeightMeters;
        set => SetField(ref _eyeHeightMeters, value);
    }

    /// <summary>Upper bound for the eye-height slider: the room's ceiling height.</summary>
    public double EyeHeightMaxMeters
    {
        get => _eyeHeightMaxMeters;
        private set => SetField(ref _eyeHeightMaxMeters, value);
    }

    public bool IsMetric => _unitSystem == UnitSystem.Metric;
    public bool IsImperial => _unitSystem == UnitSystem.Imperial;
    public string UnitHint => IsMetric ? "meters (e.g. 5 or 250 cm)" : "feet (e.g. 16 or 8' 2\")";

    /// <summary>Name for the next box added to the room. Empty falls back to "Box".</summary>
    public string NewItemName { get => _newItemName; set => SetField(ref _newItemName, value); }

    public string WidthText { get => _widthText; set => SetField(ref _widthText, value); }
    public string LengthText { get => _lengthText; set => SetField(ref _lengthText, value); }
    public string HeightText { get => _heightText; set => SetField(ref _heightText, value); }

    public string TotalAreaText { get => _totalAreaText; private set => SetField(ref _totalAreaText, value); }
    public string UsedAreaText { get => _usedAreaText; private set => SetField(ref _usedAreaText, value); }
    public string FreeAreaText { get => _freeAreaText; private set => SetField(ref _freeAreaText, value); }
    public double FreeFraction { get => _freeFraction; private set => SetField(ref _freeFraction, value); }

    public string? DimensionError
    {
        get => _dimensionError;
        private set
        {
            if (SetField(ref _dimensionError, value))
                OnPropertyChanged(nameof(HasDimensionError));
        }
    }

    public bool HasDimensionError => !string.IsNullOrEmpty(_dimensionError);

    public RoomItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetField(ref _selectedItem, value))
            {
                RemoveSelectedCommand.RaiseCanExecuteChanged();
                ApplyItemCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(HasSelection));
                RefreshItemTexts();
            }
        }
    }

    public bool HasSelection => _selectedItem is not null;

    /// <summary>When true, dragging and resizing snap to <see cref="SnapStepMeters"/>.</summary>
    public bool SnapEnabled
    {
        get => _snapEnabled;
        set => SetField(ref _snapEnabled, value);
    }

    public string ItemWidthText { get => _itemWidthText; set => SetField(ref _itemWidthText, value); }
    public string ItemDepthText { get => _itemDepthText; set => SetField(ref _itemDepthText, value); }
    public string ItemHeightText { get => _itemHeightText; set => SetField(ref _itemHeightText, value); }
    public string ItemRotationText { get => _itemRotationText; set => SetField(ref _itemRotationText, value); }

    public string? ItemError
    {
        get => _itemError;
        private set
        {
            if (SetField(ref _itemError, value))
                OnPropertyChanged(nameof(HasItemError));
        }
    }

    public bool HasItemError => !string.IsNullOrEmpty(_itemError);

    public WallOpening? SelectedOpening
    {
        get => _selectedOpening;
        set
        {
            if (SetField(ref _selectedOpening, value))
            {
                RemoveOpeningCommand.RaiseCanExecuteChanged();
                ApplyOpeningCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(HasOpeningSelection));
                RefreshOpeningTexts();
            }
        }
    }

    public bool HasOpeningSelection => _selectedOpening is not null;

    /// <summary>True when the selected opening is a window (only then is the sill editable).</summary>
    public bool OpeningIsWindow => _selectedOpening?.Kind == OpeningKind.Window;

    public WallSide OpeningWall { get => _openingWall; set => SetField(ref _openingWall, value); }
    public HingeSide OpeningHinge { get => _openingHinge; set => SetField(ref _openingHinge, value); }
    public string OpeningOffsetText { get => _openingOffsetText; set => SetField(ref _openingOffsetText, value); }
    public string OpeningWidthText { get => _openingWidthText; set => SetField(ref _openingWidthText, value); }
    public string OpeningHeightText { get => _openingHeightText; set => SetField(ref _openingHeightText, value); }
    public string OpeningSillText { get => _openingSillText; set => SetField(ref _openingSillText, value); }
    public string FrameTopText { get => _frameTopText; set => SetField(ref _frameTopText, value); }
    public string FrameBottomText { get => _frameBottomText; set => SetField(ref _frameBottomText, value); }
    public string FrameLeftText { get => _frameLeftText; set => SetField(ref _frameLeftText, value); }
    public string FrameRightText { get => _frameRightText; set => SetField(ref _frameRightText, value); }

    public string? OpeningError
    {
        get => _openingError;
        private set
        {
            if (SetField(ref _openingError, value))
                OnPropertyChanged(nameof(HasOpeningError));
        }
    }

    public bool HasOpeningError => !string.IsNullOrEmpty(_openingError);

    /// <summary>Snap granularity: 5 cm in metric, 1 inch in imperial.</summary>
    public double SnapStepMeters => _unitSystem == UnitSystem.Imperial ? Length.MetersPerInch : 0.05;

    /// <summary>Incremented whenever the scene changes; the floor-plan view binds to it to redraw.</summary>
    public int RenderVersion { get => _renderVersion; private set => SetField(ref _renderVersion, value); }

    /// <summary>
    /// Moves <paramref name="item"/> so its center is at <paramref name="world"/>,
    /// snapping to the grid (when enabled) and clamping the footprint inside the room.
    /// Called by the floor-plan view while the user drags.
    /// </summary>
    public void DragItemTo(RoomItem item, Vec2 world)
    {
        var x = world.X;
        var y = world.Y;
        if (_snapEnabled)
        {
            x = Snap(x);
            y = Snap(y);
        }

        // Keep the (possibly rotated) footprint within the room bounds.
        var probe = new FootprintRect(Vec2.Zero, item.Width.Meters, item.Depth.Meters, item.Rotation);
        var (minX, minY, maxX, maxY) = probe.Bounds();
        var halfX = (maxX - minX) / 2;
        var halfY = (maxY - minY) / 2;
        var roomW = Room.Dimensions.Width.Meters;
        var roomL = Room.Dimensions.Length.Meters;

        var target = new Vec2(Clamp(x, halfX, roomW - halfX), Clamp(y, halfY, roomL - halfY));
        // Nothing may sit in a door or closet-door swing path — block the move if it would.
        if (OverlapsAnySwing(new FootprintRect(target, item.Width.Meters, item.Depth.Meters, item.Rotation)))
            return;

        item.Position = target;
        Recompute();
    }

    /// <summary>True if the footprint overlaps any door/closet-door swing arc in the room.</summary>
    private bool OverlapsAnySwing(FootprintRect footprint)
    {
        var dims = Room.Dimensions;
        foreach (var opening in Room.Openings)
        {
            foreach (var arc in opening.FloorRegions(dims))
            {
                if (arc.Intersects(footprint))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Serializes the current room to <paramref name="stream"/> as a .mockroom document.</summary>
    public async Task SaveToAsync(Stream stream)
    {
        if (stream.CanSeek)
            stream.SetLength(0); // overwrite cleanly so a shorter file leaves no trailing bytes
        await _repository.SaveAsync(Room, stream);
    }

    /// <summary>Loads a room from <paramref name="stream"/> and swaps it into the editor.</summary>
    public async Task LoadFromAsync(Stream stream)
    {
        var loaded = await _repository.LoadAsync(stream);
        ApplyLoaded(loaded);
    }

    /// <summary>Replaces the edited room's contents with a loaded room, keeping the bound instance.</summary>
    private void ApplyLoaded(Room loaded)
    {
        SelectedItem = null;
        SelectedOpening = null;

        Room.Dimensions = loaded.Dimensions;
        Room.ClearItems();
        Room.ClearOpenings();
        Items.Clear();
        Openings.Clear();
        foreach (var item in loaded.Items)
        {
            Room.AddItem(item);
            Items.Add(item);
        }
        foreach (var opening in loaded.Openings)
        {
            ClampOpeningToRoom(opening);
            Room.AddOpening(opening);
            Openings.Add(opening);
        }

        SetUnitSystem(loaded.PreferredUnits);
        EyeHeightMaxMeters = Room.Dimensions.Height.Meters;
        if (_eyeHeightMeters > EyeHeightMaxMeters)
            EyeHeightMeters = EyeHeightMaxMeters;

        RefreshDimensionTexts();
        Recompute();
    }

    private double Snap(double meters) => Math.Round(meters / SnapStepMeters) * SnapStepMeters;

    private static double Clamp(double value, double min, double max)
        => max < min ? (min + max) / 2 : Math.Clamp(value, min, max);

    private void SetUnitSystem(UnitSystem system)
    {
        if (_unitSystem == system)
            return;
        _unitSystem = system;
        Room.PreferredUnits = system;
        OnPropertyChanged(nameof(IsMetric));
        OnPropertyChanged(nameof(IsImperial));
        OnPropertyChanged(nameof(UnitHint));
        OnPropertyChanged(nameof(SnapStepMeters));
        RefreshDimensionTexts();
        RefreshItemTexts();
        RefreshOpeningTexts();
        RefreshAreaTexts();
    }

    private void AddBox()
    {
        // Drop near the room center, nudging each new item so they don't stack exactly.
        var step = 0.3 * (_placementCounter++ % 5);
        var center = new Vec2(
            Room.Dimensions.Width.Meters / 2 + step,
            Room.Dimensions.Length.Meters / 2 + step);

        var name = string.IsNullOrWhiteSpace(_newItemName) ? "Box" : _newItemName.Trim();
        var side = Length.FromMeters(DefaultBoxMeters);
        var item = new BoxItem(name, ItemCategory.Custom, side, side, side)
        {
            Position = center,
            ColorHex = DefaultBoxColor,
        };

        Room.AddItem(item);
        Items.Add(item);
        SelectedItem = item;
        NewItemName = "";
        Recompute();
    }

    private void RemoveSelected()
    {
        if (SelectedItem is null)
            return;
        Room.RemoveItem(SelectedItem);
        Items.Remove(SelectedItem);
        SelectedItem = null;
        Recompute();
    }

    private void ApplyDimensions()
    {
        if (!UnitConverter.TryParse(WidthText, _unitSystem, out var width) ||
            !UnitConverter.TryParse(LengthText, _unitSystem, out var length) ||
            !UnitConverter.TryParse(HeightText, _unitSystem, out var height))
        {
            DimensionError = "Enter valid numbers for all three dimensions.";
            return;
        }

        if (width.Meters <= 0 || length.Meters <= 0 || height.Meters <= 0)
        {
            DimensionError = "Dimensions must be greater than zero.";
            return;
        }

        DimensionError = null;
        Room.Dimensions = new RoomDimensions(width, length, height);
        EyeHeightMaxMeters = height.Meters;
        if (_eyeHeightMeters > EyeHeightMaxMeters)
            EyeHeightMeters = EyeHeightMaxMeters;
        // A shorter ceiling may now clip existing openings; clamp them back under it.
        foreach (var opening in Room.Openings)
            ClampOpeningToRoom(opening);
        if (SelectedOpening is not null)
            RefreshOpeningTexts();
        Recompute();
    }

    private void ApplyItem()
    {
        var item = SelectedItem;
        if (item is null)
            return;

        if (!UnitConverter.TryParse(ItemWidthText, _unitSystem, out var width) ||
            !UnitConverter.TryParse(ItemDepthText, _unitSystem, out var depth) ||
            !UnitConverter.TryParse(ItemHeightText, _unitSystem, out var height))
        {
            ItemError = "Enter valid width, depth, and height.";
            return;
        }

        if (width.Meters <= 0 || depth.Meters <= 0 || height.Meters <= 0)
        {
            ItemError = "Item dimensions must be greater than zero.";
            return;
        }

        if (!double.TryParse(ItemRotationText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var degrees))
        {
            ItemError = "Rotation must be a number of degrees.";
            return;
        }

        var rotation = degrees * Math.PI / 180.0;
        if (OverlapsAnySwing(new FootprintRect(item.Position, width.Meters, depth.Meters, rotation)))
        {
            ItemError = "Item would block a door swing.";
            return;
        }

        ItemError = null;
        item.Width = width;
        item.Depth = depth;
        item.Height = height;
        item.Rotation = rotation;

        // Re-clamp the resized footprint back inside the room, then recompute.
        DragItemTo(item, item.Position);
    }

    private void AddOpening(OpeningKind kind)
    {
        var wall = OpeningWall;
        var wallLength = WallLengthFor(wall);
        var (width, height, sill) = kind switch
        {
            OpeningKind.ClosetDoor => (DefaultClosetWidthMeters, DefaultDoorHeightMeters, 0.0),
            OpeningKind.Window => (DefaultWindowWidthMeters, DefaultWindowHeightMeters, DefaultWindowSillMeters),
            _ => (DefaultDoorWidthMeters, DefaultDoorHeightMeters, 0.0),
        };
        // Windows get a default frame on each side; keep the whole opening within the wall.
        var frame = kind == OpeningKind.Window ? DefaultWindowFrameMeters : 0.0;
        width = Math.Min(width, Math.Max(0.1, wallLength - 2 * frame));

        var opening = new WallOpening(kind, wall,
            Length.FromMeters(wallLength / 2),
            Length.FromMeters(width),
            Length.FromMeters(height),
            Length.FromMeters(sill))
        {
            FrameTop = Length.FromMeters(frame),
            FrameBottom = Length.FromMeters(frame),
            FrameLeft = Length.FromMeters(frame),
            FrameRight = Length.FromMeters(frame),
        };
        ClampOpeningToRoom(opening);

        Room.AddOpening(opening);
        Openings.Add(opening);
        SelectedOpening = opening;
        Recompute();
    }

    private void RemoveSelectedOpening()
    {
        if (SelectedOpening is null)
            return;
        Room.RemoveOpening(SelectedOpening);
        Openings.Remove(SelectedOpening);
        SelectedOpening = null;
        Recompute();
    }

    private void ApplyOpening()
    {
        var opening = SelectedOpening;
        if (opening is null)
            return;

        if (!UnitConverter.TryParse(OpeningOffsetText, _unitSystem, out var offset) ||
            !UnitConverter.TryParse(OpeningWidthText, _unitSystem, out var width) ||
            !UnitConverter.TryParse(OpeningHeightText, _unitSystem, out var height))
        {
            OpeningError = "Enter valid offset, width, and height.";
            return;
        }

        // Windows can sit above a sill and carry a frame; doors/closets start at the floor with no frame.
        var sill = Length.Zero;
        var frameTop = Length.Zero;
        var frameBottom = Length.Zero;
        var frameLeft = Length.Zero;
        var frameRight = Length.Zero;
        if (opening.Kind == OpeningKind.Window)
        {
            if (!UnitConverter.TryParse(OpeningSillText, _unitSystem, out sill))
            {
                OpeningError = "Enter a valid sill height.";
                return;
            }
            if (!UnitConverter.TryParse(FrameTopText, _unitSystem, out frameTop) ||
                !UnitConverter.TryParse(FrameBottomText, _unitSystem, out frameBottom) ||
                !UnitConverter.TryParse(FrameLeftText, _unitSystem, out frameLeft) ||
                !UnitConverter.TryParse(FrameRightText, _unitSystem, out frameRight))
            {
                OpeningError = "Enter valid frame widths.";
                return;
            }
        }

        if (width.Meters <= 0 || height.Meters <= 0 || offset.Meters < 0 || sill.Meters < 0 ||
            frameTop.Meters < 0 || frameBottom.Meters < 0 || frameLeft.Meters < 0 || frameRight.Meters < 0)
        {
            OpeningError = "Sizes must be positive.";
            return;
        }

        var outerWidth = width.Meters + frameLeft.Meters + frameRight.Meters;
        var outerHeight = height.Meters + frameTop.Meters + frameBottom.Meters;

        var wallLength = WallLengthFor(OpeningWall);
        if (outerWidth > wallLength ||
            offset.Meters - outerWidth / 2 < 0 ||
            offset.Meters + outerWidth / 2 > wallLength)
        {
            OpeningError = "The opening does not fit within the wall.";
            return;
        }

        if (sill.Meters + outerHeight > Room.Dimensions.Height.Meters)
        {
            OpeningError = "The opening's top cannot exceed the wall height.";
            return;
        }

        OpeningError = null;
        opening.Wall = OpeningWall;
        opening.HingeSide = OpeningHinge;
        opening.OffsetAlongWall = offset;
        opening.Width = width;
        opening.Height = height;
        opening.SillHeight = sill;
        opening.FrameTop = frameTop;
        opening.FrameBottom = frameBottom;
        opening.FrameLeft = frameLeft;
        opening.FrameRight = frameRight;
        Recompute();
    }

    private void RefreshOpeningTexts()
    {
        var opening = SelectedOpening;
        OnPropertyChanged(nameof(OpeningIsWindow));
        if (opening is null)
        {
            OpeningOffsetText = OpeningWidthText = OpeningHeightText = OpeningSillText = "";
            FrameTopText = FrameBottomText = FrameLeftText = FrameRightText = "";
            OpeningError = null;
            return;
        }

        OpeningWall = opening.Wall;
        OpeningHinge = opening.HingeSide;
        OpeningOffsetText = FormatPlain(opening.OffsetAlongWall);
        OpeningWidthText = FormatPlain(opening.Width);
        OpeningHeightText = FormatPlain(opening.Height);
        OpeningSillText = FormatPlain(opening.SillHeight);
        FrameTopText = FormatPlain(opening.FrameTop);
        FrameBottomText = FormatPlain(opening.FrameBottom);
        FrameLeftText = FormatPlain(opening.FrameLeft);
        FrameRightText = FormatPlain(opening.FrameRight);
        OpeningError = null;
    }

    /// <summary>Shrinks an opening so its (framed) top never exceeds the current ceiling height.</summary>
    private void ClampOpeningToRoom(WallOpening opening)
    {
        var ceiling = Room.Dimensions.Height.Meters;
        if (opening.SillHeight.Meters > ceiling)
            opening.SillHeight = Length.FromMeters(ceiling);
        var frames = opening.FrameTop.Meters + opening.FrameBottom.Meters;
        var maxHeight = ceiling - opening.SillHeight.Meters - frames;
        if (opening.Height.Meters > maxHeight)
            opening.Height = Length.FromMeters(Math.Max(0, maxHeight));
    }

    /// <summary>The interior length of a wall: room width for North/South, room length for East/West.</summary>
    private double WallLengthFor(WallSide wall) =>
        wall is WallSide.North or WallSide.South ? Room.Dimensions.Width.Meters : Room.Dimensions.Length.Meters;

    private void RefreshDimensionTexts()
    {
        WidthText = FormatPlain(Room.Dimensions.Width);
        LengthText = FormatPlain(Room.Dimensions.Length);
        HeightText = FormatPlain(Room.Dimensions.Height);
    }

    private void RefreshItemTexts()
    {
        var item = SelectedItem;
        if (item is null)
        {
            ItemWidthText = ItemDepthText = ItemHeightText = ItemRotationText = "";
            ItemError = null;
            return;
        }

        ItemWidthText = FormatPlain(item.Width);
        ItemDepthText = FormatPlain(item.Depth);
        ItemHeightText = FormatPlain(item.Height);
        ItemRotationText = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0:0.#}", item.Rotation * 180.0 / Math.PI);
        ItemError = null;
    }

    // Editable fields show a bare value matching the active unit (no unit suffix in the box).
    private string FormatPlain(Length length) => _unitSystem == UnitSystem.Imperial
        ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.##}", length.Feet)
        : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.##}", length.Meters);

    private void Recompute()
    {
        LastReport = _calculator.Compute(Room);
        RefreshAreaTexts();
        RenderVersion++;
    }

    private void RefreshAreaTexts()
    {
        var report = LastReport;
        if (report is null)
            return;
        TotalAreaText = _formatter.FormatArea(report.Total, _unitSystem);
        UsedAreaText = _formatter.FormatArea(report.Used, _unitSystem);
        FreeAreaText = _formatter.FormatArea(report.Free, _unitSystem);
        FreeFraction = report.FreeFraction;
    }
}
