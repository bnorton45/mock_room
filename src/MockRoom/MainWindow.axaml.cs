using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MockRoom.ViewModels;

namespace MockRoom;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType MockRoomFileType = new("MockRoom layout")
    {
        Patterns = ["*.mockroom"],
    };

    private readonly RoomEditorViewModel _viewModel;
    // Guard against the ColorPicker ↔ ViewModel feedback loop.
    private bool _syncingColor;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new RoomEditorViewModel();
        DataContext = _viewModel;

        Plan.ItemDragged += (item, position) => _viewModel.DragItemTo(item, position);

        // ColorPicker → ViewModel: push user's choice to the paint target.
        ColorPicker.ColorChanged += (_, e) =>
        {
            if (_syncingColor) return;
            _syncingColor = true;
            _viewModel.SelectedColor = e.NewColor;
            _syncingColor = false;
        };

        // ViewModel → ColorPicker: when the selection changes, seed the picker with
        // the target's existing color so the ring starts at the right position.
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RoomEditorViewModel.SelectedColor) && !_syncingColor)
                ColorPicker.Color = _viewModel.SelectedColor;
        };

        OpenButton.Click += OnOpenClick;
        SaveButton.Click += OnSaveClick;

        // The GL viewport can't be hit-tested, so the transparent overlay feeds it pointer input.
        View3DInput.PointerPressed += (_, e) =>
        {
            View3D.BeginDrag(e.GetPosition(View3DInput));
            e.Pointer.Capture(View3DInput);
        };
        View3DInput.PointerMoved += (_, e) => View3D.DragTo(e.GetPosition(View3DInput));
        View3DInput.PointerReleased += (_, e) =>
        {
            View3D.EndDrag();
            e.Pointer.Capture(null);
        };
        View3DInput.PointerWheelChanged += (_, e) => View3D.ZoomBy(e.Delta.Y);

        // Give the label overlay a reference to the viewport so it can project world
        // positions to screen, and repaint it whenever the camera moves.
        Labels3D.Viewport = View3D;
        View3D.PropertyChanged += (_, e) =>
        {
            if (e.Property == Controls.Viewport3DControl.CameraVersionProperty)
                Labels3D.InvalidateVisual();
        };
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open room layout",
            AllowMultiple = false,
            FileTypeFilter = [MockRoomFileType],
        });
        if (files.Count == 0)
            return;

        try
        {
            await using var stream = await files[0].OpenReadAsync();
            await _viewModel.LoadFromAsync(stream);
        }
        catch (Exception ex)
        {
            // A malformed or unreadable file shouldn't crash the app; surface it on the status line.
            StatusText.Text = $"Open failed: {ex.Message}";
        }
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save room layout",
            SuggestedFileName = "room.mockroom",
            DefaultExtension = "mockroom",
            FileTypeChoices = [MockRoomFileType],
        });
        if (file is null)
            return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await _viewModel.SaveToAsync(stream);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save failed: {ex.Message}";
        }
    }
}
