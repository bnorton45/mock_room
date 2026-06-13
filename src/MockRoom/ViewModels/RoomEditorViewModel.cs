using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media;
using MockRoom.Core.Geometry;
using MockRoom.Core.Items;
using MockRoom.Core.Persistence;
using MockRoom.Core.Rendering;
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

    private PaintTarget? _selectedTarget;
    private bool _syncingSelection;
    private bool _colorPickerOpen;

    private bool _is3D;
    private Core.Rendering.CameraMode _cameraMode = Core.Rendering.CameraMode.FirstPerson;
    private double _eyeHeightMeters = 1.6;
    private double _eyeHeightMaxMeters = 2.5;
    private SpaceReport? _lastReport;

    private IReadOnlyList<Core.Rendering.CameraViewpoint> _viewpoints = [];
    private int _viewpointIndex;
    private string _viewpointLabel = "Center";
    private double _viewpointX;
    private double _viewpointZ;
    private double _viewpointYaw;
    private int _viewpointVersion;

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
        ToggleColorPickerCommand = new RelayCommand(_ => IsColorPickerOpen = !_colorPickerOpen);
        SetFirstPersonCommand = new RelayCommand(_ => CameraMode = Core.Rendering.CameraMode.FirstPerson);
        SetOrbitCommand = new RelayCommand(_ => CameraMode = Core.Rendering.CameraMode.Orbit);
        AddDoorCommand = new RelayCommand(_ => AddOpening(OpeningKind.Door));
        AddClosetDoorCommand = new RelayCommand(_ => AddOpening(OpeningKind.ClosetDoor));
        AddWindowCommand = new RelayCommand(_ => AddOpening(OpeningKind.Window));
        RemoveOpeningCommand = new RelayCommand(_ => RemoveSelectedOpening(), _ => SelectedOpening is not null);
        ApplyOpeningCommand = new RelayCommand(_ => ApplyOpening(), _ => SelectedOpening is not null);
        NextViewpointCommand = new RelayCommand(_ => NavigateViewpoint(+1));
        PrevViewpointCommand = new RelayCommand(_ => NavigateViewpoint(-1));

        _eyeHeightMaxMeters = Room.Dimensions.Height.Meters;
        RefreshDimensionTexts();
        RebuildViewpoints();
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
    public RelayCommand NextViewpointCommand { get; }
    public RelayCommand PrevViewpointCommand { get; }

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

    /// <summary>Display label for the active viewpoint, e.g. "Wall 2 (4/9)".</summary>
    public string ViewpointLabel { get => _viewpointLabel; private set => SetField(ref _viewpointLabel, value); }

    /// <summary>World-space X of the active first-person viewpoint.</summary>
    public double ViewpointX { get => _viewpointX; private set => SetField(ref _viewpointX, value); }

    /// <summary>World-space Z of the active first-person viewpoint.</summary>
    public double ViewpointZ { get => _viewpointZ; private set => SetField(ref _viewpointZ, value); }

    /// <summary>Initial yaw (radians) for the active viewpoint.</summary>
    public double ViewpointYaw { get => _viewpointYaw; private set => SetField(ref _viewpointYaw, value); }

    /// <summary>Incremented whenever the active viewpoint changes; the 3D control resets its camera position.</summary>
    public int ViewpointVersion { get => _viewpointVersion; private set => SetField(ref _viewpointVersion, value); }

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

    /// <summary>
    /// The currently selected paint target (item, wall, floor, or opening). TwoWay-bound
    /// to both the 2D and 3D view controls; drives the paint panel in the sidebar.
    /// </summary>
    public PaintTarget? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (!SetField(ref _selectedTarget, value))
                return;

            // Mirror into SelectedOpening when an opening is selected so the editor populates.
            if (!_syncingSelection)
            {
                _syncingSelection = true;
                if (value is OpeningPaintTarget op)
                    SelectedOpening = op.Opening;
                _syncingSelection = false;
            }

            RemoveSelectedCommand.RaiseCanExecuteChanged();
            ApplyItemCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(HasPaintTarget));
            OnPropertyChanged(nameof(HasItemSelection));
            OnPropertyChanged(nameof(SelectedTargetLabel));
            NotifyColor();
            RefreshItemTexts();
        }
    }

    /// <summary>The selected item when the selection is an item; null otherwise.</summary>
    public RoomItem? SelectedItem => (_selectedTarget as ItemPaintTarget)?.Item;

    public bool HasSelection => SelectedItem is not null;

    /// <summary>True when any paint target is selected (item, wall, floor, or opening).</summary>
    public bool HasPaintTarget => _selectedTarget is not null;

    /// <summary>True when the selection is specifically an item (enables resize controls).</summary>
    public bool HasItemSelection => _selectedTarget is ItemPaintTarget;

    /// <summary>Human-readable label for what is selected, shown above the color picker.</summary>
    public string SelectedTargetLabel => _selectedTarget switch
    {
        FloorPaintTarget                 => "Floor",
        WallPaintTarget wt               => $"{wt.Side} Wall",
        OpeningPaintTarget op            => $"{op.Opening.Kind} ({op.Opening.Wall} wall)",
        ItemPaintTarget { Item: var it } => it.Name,
        _                                => "Nothing selected",
    };

    // ── Unified paint color ───────────────────────────────────────────────────

    /// <summary>
    /// The colour of the currently selected paint target. Setting this updates the
    /// target's hex string and redraws the scene.
    /// </summary>
    public Color SelectedColor
    {
        get => _selectedTarget switch
        {
            FloorPaintTarget                 => ParseHex(Room.Surfaces.FloorColorHex),
            WallPaintTarget wt               => ParseHex(Room.Surfaces.WallColorFor(wt.Side)),
            OpeningPaintTarget op            => ParseHex(op.Opening.ColorHex),
            ItemPaintTarget { Item: var it } => ParseHex(it.ColorHex),
            _                                => ParseHex("#9AA0A6"),
        };
        set
        {
            var hex = ColorToHex(value);
            switch (_selectedTarget)
            {
                case FloorPaintTarget:
                    Room.Surfaces = Room.Surfaces with { FloorColorHex = hex };
                    break;
                case WallPaintTarget wt:
                    Room.Surfaces = Room.Surfaces.WithWallColor(wt.Side, hex);
                    break;
                case OpeningPaintTarget op:
                    op.Opening.ColorHex = hex;
                    break;
                case ItemPaintTarget { Item: var it }:
                    it.ColorHex = hex;
                    break;
                default:
                    return;
            }
            Recompute();
            NotifyColor();
        }
    }

    /// <summary>Brush for the color swatch button. Updated whenever SelectedColor changes.</summary>
    public ISolidColorBrush SelectedColorBrush => new SolidColorBrush(SelectedColor);

    /// <summary>Whether the color wheel panel is visible in the sidebar.</summary>
    public bool IsColorPickerOpen
    {
        get => _colorPickerOpen;
        private set => SetField(ref _colorPickerOpen, value);
    }

    public RelayCommand ToggleColorPickerCommand { get; }

    // ── Item material (metallic / roughness) — still per-item only ────────────

    public double ItemMetallic
    {
        get => SelectedItem?.Metallic ?? 0.0;
        set
        {
            if (SelectedItem is { } item)
            {
                item.Metallic = (float)Math.Clamp(value, 0.0, 1.0);
                Recompute();
            }
        }
    }

    public double ItemRoughness
    {
        get => SelectedItem?.Roughness ?? 0.8;
        set
        {
            if (SelectedItem is { } item)
            {
                item.Roughness = (float)Math.Clamp(value, 0.0, 1.0);
                Recompute();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

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
            if (!SetField(ref _selectedOpening, value))
                return;
            RemoveOpeningCommand.RaiseCanExecuteChanged();
            ApplyOpeningCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(HasOpeningSelection));
            RefreshOpeningTexts();
            // Mirror into SelectedTarget so the paint panel shows the opening's colour.
            if (!_syncingSelection)
            {
                _syncingSelection = true;
                SelectedTarget = value is null ? null : new OpeningPaintTarget(value);
                _syncingSelection = false;
            }
        }
    }

    public bool HasOpeningSelection => _selectedOpening is not null;

    /// <summary>True when the selected opening is a window (only then is the sill and bottom frame editable).</summary>
    public bool OpeningIsWindow => _selectedOpening?.Kind == OpeningKind.Window;

    /// <summary>True when the selected opening carries a frame (doors and windows, not closet doors).</summary>
    public bool OpeningHasFrame => _selectedOpening?.Kind is OpeningKind.Door or OpeningKind.Window;

    /// <summary>True when the selected opening has a hinge side to configure (doors and closet doors, not windows).</summary>
    public bool OpeningHasHinge => _selectedOpening?.Kind is OpeningKind.Door or OpeningKind.ClosetDoor;

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
        SelectedTarget = null;
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

        SyncSurfaceFields();
        RefreshDimensionTexts();
        RebuildViewpoints();
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
        SelectedTarget = new ItemPaintTarget(item);
        NewItemName = "";
        Recompute();
    }

    private void RemoveSelected()
    {
        if (SelectedItem is null)
            return;
        Room.RemoveItem(SelectedItem);
        Items.Remove(SelectedItem);
        SelectedTarget = null;
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
        RebuildViewpoints();
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
        // Doors and windows get a default frame; closet doors do not.
        // Doors have no bottom frame (the floor acts as the threshold).
        var frame = kind is OpeningKind.Window or OpeningKind.Door ? DefaultWindowFrameMeters : 0.0;
        width = Math.Min(width, Math.Max(0.1, wallLength - 2 * frame));

        var opening = new WallOpening(kind, wall,
            Length.FromMeters(wallLength / 2),
            Length.FromMeters(width),
            Length.FromMeters(height),
            Length.FromMeters(sill))
        {
            FrameTop = Length.FromMeters(frame),
            FrameBottom = Length.FromMeters(kind == OpeningKind.Window ? frame : 0.0),
            FrameLeft = Length.FromMeters(frame),
            FrameRight = Length.FromMeters(frame),
        };
        ClampOpeningToRoom(opening);

        if (!PlaceOpeningOnWall(opening, wallLength))
        {
            OpeningError = $"No room on the {wall} wall for another opening.";
            return;
        }

        OpeningError = null;
        Room.AddOpening(opening);
        Openings.Add(opening);
        SelectedOpening = opening;
        Recompute();
    }

    /// <summary>
    /// Finds the gap on the opening's wall that is closest to the wall centre and wide
    /// enough to fit the opening, then positions the opening there.  Returns false if no
    /// gap exists (caller should report the error and not add the opening).
    /// </summary>
    private bool PlaceOpeningOnWall(WallOpening candidate, double wallLength)
    {
        var outerWidth = candidate.OuterWidth.Meters;
        if (outerWidth > wallLength)
            return false;

        // Collect occupied ranges on the same wall sorted by left edge.
        var occupied = new List<(double Left, double Right)>();
        foreach (var o in Room.Openings)
        {
            if (o.Wall != candidate.Wall) continue;
            occupied.Add((
                o.OffsetAlongWall.Meters - o.OuterWidth.Meters / 2,
                o.OffsetAlongWall.Meters + o.OuterWidth.Meters / 2));
        }
        occupied.Sort((a, b) => a.Left.CompareTo(b.Left));

        // Walk the occupied spans and collect every gap wide enough for outerWidth.
        var wallCenter = wallLength / 2;
        var bestGapLeft = double.NaN;
        var bestGapRight = double.NaN;
        var bestDist = double.MaxValue;

        var cursor = 0.0;
        foreach (var (oLeft, oRight) in occupied)
        {
            var gapRight = oLeft;
            if (gapRight - cursor >= outerWidth)
            {
                var dist = Math.Abs((cursor + gapRight) / 2 - wallCenter);
                if (dist < bestDist) { bestDist = dist; bestGapLeft = cursor; bestGapRight = gapRight; }
            }
            cursor = Math.Max(cursor, oRight);
        }
        // Trailing gap after the last existing opening.
        if (wallLength - cursor >= outerWidth)
        {
            var dist = Math.Abs((cursor + wallLength) / 2 - wallCenter);
            if (dist < bestDist) { bestGapLeft = cursor; bestGapRight = wallLength; }
        }

        if (double.IsNaN(bestGapLeft))
            return false;

        // Centre the opening inside the chosen gap, clamped to stay within it.
        var gapMid = (bestGapLeft + bestGapRight) / 2;
        var offset = Math.Clamp(gapMid, bestGapLeft + outerWidth / 2, bestGapRight - outerWidth / 2);
        candidate.OffsetAlongWall = Length.FromMeters(offset);
        return true;
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

        // Windows sit above a sill and frame all four sides.
        // Doors frame top, left, and right only (floor is the threshold — no bottom frame).
        // Closet doors have no frame.
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
        else if (opening.Kind == OpeningKind.Door)
        {
            if (!UnitConverter.TryParse(FrameTopText, _unitSystem, out frameTop) ||
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

        // offset is the start-edge position (distance from wall start to the opening's near edge).
        var wallLength = WallLengthFor(OpeningWall);
        if (outerWidth > wallLength ||
            offset.Meters < 0 ||
            offset.Meters + outerWidth > wallLength)
        {
            OpeningError = "The opening does not fit within the wall.";
            return;
        }

        if (sill.Meters + outerHeight > Room.Dimensions.Height.Meters)
        {
            OpeningError = "The opening's top cannot exceed the wall height.";
            return;
        }

        // Block placement that overlaps another opening on the same wall.
        var newLeft = offset.Meters;
        var newRight = offset.Meters + outerWidth;
        foreach (var other in Room.Openings)
        {
            if (ReferenceEquals(other, opening)) continue;
            if (other.Wall != OpeningWall) continue;
            var otherLeft = other.OffsetAlongWall.Meters - other.OuterWidth.Meters / 2;
            var otherRight = other.OffsetAlongWall.Meters + other.OuterWidth.Meters / 2;
            if (newLeft < otherRight && newRight > otherLeft)
            {
                OpeningError = "The opening overlaps another opening on this wall.";
                return;
            }
        }

        OpeningError = null;
        opening.Wall = OpeningWall;
        opening.HingeSide = OpeningHinge;
        // Convert back from start-edge to center-based offset stored in the domain model.
        opening.OffsetAlongWall = Length.FromMeters(offset.Meters + outerWidth / 2);
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
        OnPropertyChanged(nameof(OpeningHasFrame));
        OnPropertyChanged(nameof(OpeningHasHinge));
        if (opening is null)
        {
            OpeningOffsetText = OpeningWidthText = OpeningHeightText = OpeningSillText = "";
            FrameTopText = FrameBottomText = FrameLeftText = FrameRightText = "";
            OpeningError = null;
            return;
        }

        OpeningWall = opening.Wall;
        OpeningHinge = opening.HingeSide;
        // Display the start-edge offset (center − half outer width) so the user can type 0 to push the opening flush against the wall corner.
        OpeningOffsetText = FormatPlain(Length.FromMeters(opening.OffsetAlongWall.Meters - opening.OuterWidth.Meters / 2));
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
        }
        else
        {
            ItemWidthText = FormatPlain(item.Width);
            ItemDepthText = FormatPlain(item.Depth);
            ItemHeightText = FormatPlain(item.Height);
            ItemRotationText = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:0.#}", item.Rotation * 180.0 / Math.PI);
            ItemError = null;
        }

        // Notify sliders and the unified color panel.
        OnPropertyChanged(nameof(ItemMetallic));
        OnPropertyChanged(nameof(ItemRoughness));
        NotifyColor();
    }

    // Editable fields show a bare value matching the active unit (no unit suffix in the box).
    private string FormatPlain(Length length) => _unitSystem == UnitSystem.Imperial
        ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.##}", length.Feet)
        : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.##}", length.Meters);

    private static Color ParseHex(string hex)
    {
        if (Color.TryParse(hex, out var c))
            return c;
        return Color.Parse("#9AA0A6");
    }

    private static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private void NotifyColor()
    {
        OnPropertyChanged(nameof(SelectedColor));
        OnPropertyChanged(nameof(SelectedColorBrush));
    }

    /// <summary>
    /// After loading, the selected target may have been cleared.  If it was pointing at a
    /// surface, notify the colour property so the panel refreshes.
    /// </summary>
    private void SyncSurfaceFields() => NotifyColor();

    private void RebuildViewpoints()
    {
        _viewpoints = Core.Rendering.Camera.BuildViewpoints(
            (float)Room.Dimensions.Width.Meters,
            (float)Room.Dimensions.Length.Meters);
        _viewpointIndex = 0;
        ApplyViewpoint();
    }

    private void NavigateViewpoint(int delta)
    {
        if (_viewpoints.Count == 0) return;
        _viewpointIndex = (_viewpointIndex + delta + _viewpoints.Count) % _viewpoints.Count;
        ApplyViewpoint();
    }

    private void ApplyViewpoint()
    {
        if (_viewpoints.Count == 0) return;
        var vp = _viewpoints[_viewpointIndex];
        ViewpointX = vp.X;
        ViewpointZ = vp.Z;
        ViewpointYaw = vp.Yaw;
        ViewpointLabel = $"{vp.Name} ({_viewpointIndex + 1}/{_viewpoints.Count})";
        ViewpointVersion++;
    }

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
