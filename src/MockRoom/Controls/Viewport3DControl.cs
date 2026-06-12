using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using MockRoom.Core.Items;
using MockRoom.Core.Rendering;
using MockRoom.Core.Rooms;
using MockRoom.Core.Spatial;
using Silk.NET.OpenGL;

namespace MockRoom.Controls;

/// <summary>
/// Hardware-accelerated 3D view of the room on Silk.NET GL via Avalonia's
/// <see cref="OpenGlControlBase"/>. Renders the floor, walls (with door openings),
/// and each item as a colored cuboid, lit by a single direction for depth cues.
///
/// The camera supports two modes (<see cref="MockRoom.Core.Rendering.CameraMode"/>):
/// first-person (stand at the room center, drag to look around, slider for eye
/// height) and orbit (circle the room from outside, drag to orbit, wheel to zoom).
/// Camera/mesh math lives in <c>MockRoom.Core.Rendering</c>; this control owns the
/// GL resources and the draw call.
///
/// A GL control draws into its own framebuffer and paints nothing into Avalonia's
/// drawing context, so it is not hit-testable and never receives pointer events.
/// Look/zoom is therefore driven through <see cref="BeginDrag"/>/<see cref="DragTo"/>/
/// <see cref="EndDrag"/>/<see cref="ZoomBy"/>, which a transparent overlay forwards to.
/// </summary>
public sealed class Viewport3DControl : OpenGlControlBase
{
    private const float LookSensitivity = 0.005f;   // radians per pixel
    private const float OrbitZoomStep = 0.6f;        // meters per wheel notch
    private const double ClickThreshold = 4.0;       // max pointer travel (px) that still counts as a click

    public static readonly StyledProperty<Room?> RoomProperty =
        AvaloniaProperty.Register<Viewport3DControl, Room?>(nameof(Room));

    public static readonly StyledProperty<int> RenderVersionProperty =
        AvaloniaProperty.Register<Viewport3DControl, int>(nameof(RenderVersion));

    public static readonly StyledProperty<CameraMode> CameraModeProperty =
        AvaloniaProperty.Register<Viewport3DControl, CameraMode>(nameof(CameraMode));

    public static readonly StyledProperty<double> EyeHeightProperty =
        AvaloniaProperty.Register<Viewport3DControl, double>(
            nameof(EyeHeight), defaultValue: 1.6, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<SpaceReport?> SpaceReportProperty =
        AvaloniaProperty.Register<Viewport3DControl, SpaceReport?>(nameof(SpaceReport));

    public static readonly StyledProperty<RoomItem?> SelectedItemProperty =
        AvaloniaProperty.Register<Viewport3DControl, RoomItem?>(
            nameof(SelectedItem), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private GL? _gl;
    private uint _program;
    private uint _vao;
    private uint _vbo;
    private int _mvpLocation = -1;
    private int _lightLocation = -1;

    private Camera? _camera;
    private int _vertexCount;
    private int _uploadedVersion = -1;
    private bool _meshDirty = true;

    private Point _lastPointer;
    private Point _pressOrigin;
    private bool _dragging;
    private bool _maybeClick;

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

    public CameraMode CameraMode
    {
        get => GetValue(CameraModeProperty);
        set => SetValue(CameraModeProperty, value);
    }

    public double EyeHeight
    {
        get => GetValue(EyeHeightProperty);
        set => SetValue(EyeHeightProperty, value);
    }

    /// <summary>Latest space report; its grid paints the blue free-floor overlay.</summary>
    public SpaceReport? SpaceReport
    {
        get => GetValue(SpaceReportProperty);
        set => SetValue(SpaceReportProperty, value);
    }

    /// <summary>The selected item; highlighted in the mesh and set by ray-picking on click.</summary>
    public RoomItem? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RoomProperty || change.Property == RenderVersionProperty ||
            change.Property == SpaceReportProperty || change.Property == SelectedItemProperty)
        {
            _meshDirty = true;
        }

        if (change.Property == RoomProperty || change.Property == RenderVersionProperty ||
            change.Property == SpaceReportProperty || change.Property == SelectedItemProperty ||
            change.Property == CameraModeProperty || change.Property == EyeHeightProperty)
        {
            RequestNextFrameRendering();
        }
    }

    protected override unsafe void OnOpenGlInit(GlInterface gl)
    {
        _gl = GL.GetApi(name => gl.GetProcAddress(name));

        _program = BuildProgram(_gl);
        _mvpLocation = _gl.GetUniformLocation(_program, "uMvp");
        _lightLocation = _gl.GetUniformLocation(_program, "uLightDir");

        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);
        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        var stride = (uint)(RoomMeshBuilder.FloatsPerVertex * sizeof(float));
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));

        _gl.Enable(EnableCap.DepthTest);
        _meshDirty = true;
        _uploadedVersion = -1;
    }

    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        var glApi = _gl;
        var room = Room;
        if (glApi is null || room is null)
            return;

        var scaling = (TopLevel.GetTopLevel(this)?.RenderScaling) ?? 1.0;
        var pixelWidth = (uint)Math.Max(1, Bounds.Width * scaling);
        var pixelHeight = (uint)Math.Max(1, Bounds.Height * scaling);
        glApi.Viewport(0, 0, pixelWidth, pixelHeight);

        glApi.ClearColor(0.066f, 0.082f, 0.102f, 1f); // matches the app's #11151A canvas
        glApi.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        glApi.Enable(EnableCap.DepthTest);

        EnsureCamera(room);
        UploadMeshIfNeeded(glApi, room);
        if (_vertexCount == 0)
            return;

        glApi.UseProgram(_program);

        var aspect = (float)(Bounds.Width <= 0 ? 1.0 : Bounds.Width / Math.Max(1.0, Bounds.Height));
        var mvp = _camera!.ViewProjection(aspect);
        // Upload row-major fields with transpose=false; GL reads them as the column-major
        // transpose, which is exactly what a column-vector shader expects (see Camera.ViewProjection).
        var span = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<System.Numerics.Matrix4x4, float>(ref mvp), 16);
        glApi.UniformMatrix4(_mvpLocation, 1, false, span);
        glApi.Uniform3(_lightLocation, 0.4f, 1.0f, 0.7f);

        glApi.BindVertexArray(_vao);
        glApi.DrawArrays(PrimitiveType.Triangles, 0, (uint)_vertexCount);
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        var glApi = _gl;
        if (glApi is null)
            return;

        if (_vbo != 0) glApi.DeleteBuffer(_vbo);
        if (_vao != 0) glApi.DeleteVertexArray(_vao);
        if (_program != 0) glApi.DeleteProgram(_program);
        _vbo = _vao = _program = 0;
        glApi.Dispose();
        _gl = null;
    }

    private void EnsureCamera(Room room)
    {
        if (_camera is null)
            _camera = Camera.FromRoom(room.Dimensions);
        else
            _camera.SetRoom(room.Dimensions);

        _camera.Mode = CameraMode;
        _camera.EyeHeight = (float)EyeHeight;
    }

    private unsafe void UploadMeshIfNeeded(GL glApi, Room room)
    {
        if (!_meshDirty && _uploadedVersion == RenderVersion)
            return;

        var mesh = RoomMeshBuilder.Build(room, SpaceReport?.Grid, SelectedItem);
        _vertexCount = mesh.VertexCount;

        glApi.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        ReadOnlySpan<float> data = mesh.Vertices;
        glApi.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)),
            data, BufferUsageARB.DynamicDraw);

        _meshDirty = false;
        _uploadedVersion = RenderVersion;
    }

    // --- input (forwarded from the overlay) ------------------------------

    /// <summary>Starts a look/orbit drag at the given control-space point.</summary>
    public void BeginDrag(Point position)
    {
        _dragging = true;
        _maybeClick = true; // a press that barely moves is treated as a pick, not a look-drag
        _lastPointer = position;
        _pressOrigin = position;
    }

    /// <summary>Continues a drag: rotates the camera by the pointer delta and redraws.</summary>
    public void DragTo(Point position)
    {
        if (!_dragging || _camera is null)
            return;

        // Once the pointer travels past the click threshold, this is a look-drag, not a click.
        if (_maybeClick && Distance(position, _pressOrigin) > ClickThreshold)
            _maybeClick = false;

        var dx = (float)(position.X - _lastPointer.X);
        var dy = (float)(position.Y - _lastPointer.Y);
        _lastPointer = position;

        if (_camera.Mode == CameraMode.FirstPerson)
        {
            _camera.Yaw += dx * LookSensitivity;
            _camera.Pitch -= dy * LookSensitivity; // drag up looks up
        }
        else
        {
            _camera.OrbitYaw += dx * LookSensitivity;
            _camera.OrbitPitch += dy * LookSensitivity;
        }

        RequestNextFrameRendering();
    }

    /// <summary>Ends a drag; a press that never crossed the click threshold ray-picks an item.</summary>
    public void EndDrag()
    {
        var wasClick = _dragging && _maybeClick;
        _dragging = false;
        _maybeClick = false;
        if (wasClick)
            PickAt(_pressOrigin);
    }

    /// <summary>Casts a ray through the clicked point and selects the front-most item (or clears).</summary>
    private void PickAt(Point position)
    {
        var room = Room;
        if (_camera is null || room is null)
            return;

        var ray = _camera.ScreenPointToRay(position.X, position.Y, Bounds.Width, Bounds.Height);
        SelectedItem = RayPicker.Pick(room.Items, ray);
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Zooms the orbit camera by a mouse-wheel delta (ignored in first-person).</summary>
    public void ZoomBy(double wheelDelta)
    {
        if (_camera is null || _camera.Mode != CameraMode.Orbit)
            return;

        _camera.OrbitDistance -= (float)wheelDelta * OrbitZoomStep;
        RequestNextFrameRendering();
    }

    // --- GL program ------------------------------------------------------

    private uint BuildProgram(GL glApi)
    {
        var isEs = GlVersion.Type == GlProfileType.OpenGLES;
        var header = isEs ? "#version 300 es\nprecision mediump float;\n" : "#version 330 core\n";

        var vertex = CompileShader(glApi, ShaderType.VertexShader, header + VertexSource);
        var fragment = CompileShader(glApi, ShaderType.FragmentShader, header + FragmentSource);

        var program = glApi.CreateProgram();
        glApi.AttachShader(program, vertex);
        glApi.AttachShader(program, fragment);
        glApi.LinkProgram(program);
        glApi.GetProgram(program, ProgramPropertyARB.LinkStatus, out var linked);
        if (linked == 0)
            throw new InvalidOperationException($"Shader link failed: {glApi.GetProgramInfoLog(program)}");

        glApi.DetachShader(program, vertex);
        glApi.DetachShader(program, fragment);
        glApi.DeleteShader(vertex);
        glApi.DeleteShader(fragment);
        return program;
    }

    private static uint CompileShader(GL glApi, ShaderType type, string source)
    {
        var shader = glApi.CreateShader(type);
        glApi.ShaderSource(shader, source);
        glApi.CompileShader(shader);
        glApi.GetShader(shader, ShaderParameterName.CompileStatus, out var status);
        if (status == 0)
            throw new InvalidOperationException($"{type} compile failed: {glApi.GetShaderInfoLog(shader)}");
        return shader;
    }

    private const string VertexSource = """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec3 aColor;
        uniform mat4 uMvp;
        out vec3 vNormal;
        out vec3 vColor;
        void main()
        {
            gl_Position = uMvp * vec4(aPos, 1.0);
            vNormal = aNormal;
            vColor = aColor;
        }
        """;

    private const string FragmentSource = """
        in vec3 vNormal;
        in vec3 vColor;
        uniform vec3 uLightDir;
        out vec4 fragColor;
        void main()
        {
            float d = abs(dot(normalize(vNormal), normalize(uLightDir)));
            float light = 0.35 + 0.65 * d; // ambient + two-sided diffuse
            fragColor = vec4(vColor * light, 1.0);
        }
        """;
}
