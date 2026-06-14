using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using MockRoom.Core.Rendering;
using MockRoom.Core.Rooms;
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

    public static readonly StyledProperty<PaintTarget?> SelectedTargetProperty =
        AvaloniaProperty.Register<Viewport3DControl, PaintTarget?>(
            nameof(SelectedTarget), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// Incremented whenever the camera changes (drag, zoom, eye height, mode switch).
    /// Overlay controls that project world positions to screen can observe this to know
    /// when to repaint.
    /// </summary>
    public static readonly StyledProperty<int> CameraVersionProperty =
        AvaloniaProperty.Register<Viewport3DControl, int>(nameof(CameraVersion));

    /// <summary>World-space X override for the first-person eye position.</summary>
    public static readonly StyledProperty<double> ViewpointXProperty =
        AvaloniaProperty.Register<Viewport3DControl, double>(nameof(ViewpointX));

    /// <summary>World-space Z override for the first-person eye position.</summary>
    public static readonly StyledProperty<double> ViewpointZProperty =
        AvaloniaProperty.Register<Viewport3DControl, double>(nameof(ViewpointZ));

    /// <summary>Initial yaw for the current viewpoint, in radians.</summary>
    public static readonly StyledProperty<double> ViewpointYawProperty =
        AvaloniaProperty.Register<Viewport3DControl, double>(nameof(ViewpointYaw));

    /// <summary>
    /// Incremented by the ViewModel whenever the active viewpoint changes.
    /// The control uses this as a trigger to apply the new position and reset yaw.
    /// </summary>
    public static readonly StyledProperty<int> ViewpointVersionProperty =
        AvaloniaProperty.Register<Viewport3DControl, int>(nameof(ViewpointVersion));

    private int _appliedViewpointVersion = -1;

    private GL? _gl;
    private uint _program;
    private uint _vao;
    private uint _vbo;
    private int _mvpLocation   = -1;
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

    /// <summary>
    /// The currently selected paint target (item, wall, floor, or opening); highlighted
    /// in the mesh and set by ray-picking on click.
    /// </summary>
    public PaintTarget? SelectedTarget
    {
        get => GetValue(SelectedTargetProperty);
        set => SetValue(SelectedTargetProperty, value);
    }

    public int CameraVersion
    {
        get => GetValue(CameraVersionProperty);
        private set => SetValue(CameraVersionProperty, value);
    }

    public double ViewpointX
    {
        get => GetValue(ViewpointXProperty);
        set => SetValue(ViewpointXProperty, value);
    }

    public double ViewpointZ
    {
        get => GetValue(ViewpointZProperty);
        set => SetValue(ViewpointZProperty, value);
    }

    public double ViewpointYaw
    {
        get => GetValue(ViewpointYawProperty);
        set => SetValue(ViewpointYawProperty, value);
    }

    public int ViewpointVersion
    {
        get => GetValue(ViewpointVersionProperty);
        set => SetValue(ViewpointVersionProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RoomProperty || change.Property == RenderVersionProperty ||
            change.Property == SelectedTargetProperty)
        {
            _meshDirty = true;
        }

        if (change.Property == RoomProperty || change.Property == RenderVersionProperty ||
            change.Property == SelectedTargetProperty ||
            change.Property == CameraModeProperty || change.Property == EyeHeightProperty ||
            change.Property == ViewpointVersionProperty)
        {
            RequestNextFrameRendering();
        }

        if (change.Property == CameraModeProperty || change.Property == EyeHeightProperty)
            CameraVersion++;

        if (change.Property == ViewpointVersionProperty)
        {
            // Apply position and reset yaw immediately so the effect is visible at next render.
            if (_camera is not null)
            {
                _camera.FirstPersonX = (float)ViewpointX;
                _camera.FirstPersonZ = (float)ViewpointZ;
                _camera.Yaw = (float)ViewpointYaw;
                _appliedViewpointVersion = ViewpointVersion;
            }
            CameraVersion++;
        }
    }

    protected override unsafe void OnOpenGlInit(GlInterface gl)
    {
        _gl = GL.GetApi(name => gl.GetProcAddress(name));

        _program = BuildProgram(_gl);
        _mvpLocation     = _gl.GetUniformLocation(_program, "uMvp");
        _lightLocation   = _gl.GetUniformLocation(_program, "uLightDir");

        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);
        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        // Layout: [pos xyz (3) | normal xyz (3) | color rgb (3) | metallic (1) | roughness (1)] = 11 floats
        var stride = (uint)(RoomMeshBuilder.FloatsPerVertex * sizeof(float));
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
        _gl.EnableVertexAttribArray(3);
        _gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, stride, (void*)(9 * sizeof(float)));
        _gl.EnableVertexAttribArray(4);
        _gl.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, stride, (void*)(10 * sizeof(float)));

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
        _camera.FirstPersonX = (float)ViewpointX;
        _camera.FirstPersonZ = (float)ViewpointZ;

        // Apply the viewpoint yaw on first render or whenever the viewpoint changes.
        var ver = ViewpointVersion;
        if (ver != _appliedViewpointVersion)
        {
            _camera.Yaw = (float)ViewpointYaw;
            _appliedViewpointVersion = ver;
        }
    }

    private unsafe void UploadMeshIfNeeded(GL glApi, Room room)
    {
        if (!_meshDirty && _uploadedVersion == RenderVersion)
            return;

        var mesh = RoomMeshBuilder.Build(room, SelectedTarget);
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

        CameraVersion++;
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

    /// <summary>Casts a ray through the clicked point and selects the hit surface or item (or clears).</summary>
    private void PickAt(Point position)
    {
        var room = Room;
        if (_camera is null || room is null)
            return;

        var ray = _camera.ScreenPointToRay(position.X, position.Y, Bounds.Width, Bounds.Height);
        SelectedTarget = RayPicker.PickTarget(room, ray);
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
        CameraVersion++;
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Projects a world-space point to control-space pixel coordinates.
    /// Returns null when the point is behind the camera or the camera is not yet initialised.
    /// </summary>
    public Point? ProjectToScreen(System.Numerics.Vector3 worldPos)
    {
        if (_camera is null) return null;
        var aspect = (float)(Bounds.Width / Math.Max(1.0, Bounds.Height));
        var ndc = _camera.WorldToNdc(worldPos, aspect);
        if (ndc is null) return null;
        var screenX = (ndc.Value.X + 1f) * 0.5 * Bounds.Width;
        var screenY = (1f - ndc.Value.Y) * 0.5 * Bounds.Height;
        return new Point(screenX, screenY);
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
        {
            var log = glApi.GetProgramInfoLog(program);
            Console.Error.WriteLine($"[Viewport3D] Shader link failed: {log}");
            throw new InvalidOperationException($"Shader link failed: {log}");
        }

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
        {
            var log = glApi.GetShaderInfoLog(shader);
            Console.Error.WriteLine($"[Viewport3D] {type} compile failed: {log}");
            throw new InvalidOperationException($"{type} compile failed: {log}");
        }
        return shader;
    }

    private const string VertexSource = """
        layout(location = 0) in vec3  aPos;
        layout(location = 1) in vec3  aNormal;
        layout(location = 2) in vec3  aColor;
        layout(location = 3) in float aMetallic;
        layout(location = 4) in float aRoughness;
        uniform mat4 uMvp;
        out vec3  vNormal;
        out vec3  vColor;
        out float vMetallic;
        out float vRoughness;
        void main()
        {
            gl_Position = uMvp * vec4(aPos, 1.0);
            vNormal    = aNormal;
            vColor     = aColor;
            vMetallic  = aMetallic;
            vRoughness = aRoughness;
        }
        """;

    private const string FragmentSource = """
        in vec3  vNormal;
        in vec3  vColor;
        in float vMetallic;
        in float vRoughness;
        uniform vec3 uLightDir;
        out vec4 fragColor;
        void main()
        {
            float d = abs(dot(normalize(vNormal), normalize(uLightDir)));
            float sharpness = mix(1.0, 8.0, 1.0 - vRoughness);
            float light = 0.2 + 0.75 * pow(d, sharpness);
            float spec = pow(d, mix(4.0, 32.0, 1.0 - vRoughness)) * vMetallic * 0.5;
            vec3 specTint = mix(vec3(0.8), vColor, vMetallic);
            vec3 color = vColor * light * (1.0 - vMetallic * 0.3) + specTint * spec;
            fragColor = vec4(color, 1.0);
        }
        """;
}
