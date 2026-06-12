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

        item.Position = new Vec2(Clamp(x, halfX, roomW - halfX), Clamp(y, halfY, roomL - halfY));
        Recompute();
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

        Room.Dimensions = loaded.Dimensions;
        Room.ClearItems();
        Room.ClearDoors();
        Items.Clear();
        foreach (var item in loaded.Items)
        {
            Room.AddItem(item);
            Items.Add(item);
        }
        foreach (var door in loaded.Doors)
            Room.AddDoor(door);

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

        ItemError = null;
        item.Width = width;
        item.Depth = depth;
        item.Height = height;
        item.Rotation = degrees * Math.PI / 180.0;

        // Re-clamp the resized footprint back inside the room, then recompute.
        DragItemTo(item, item.Position);
    }

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
