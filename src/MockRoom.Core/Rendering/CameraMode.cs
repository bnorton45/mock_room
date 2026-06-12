namespace MockRoom.Core.Rendering;

/// <summary>
/// How the 3D viewport frames the room. <see cref="FirstPerson"/> stands at the
/// room center and looks around in place; <see cref="Orbit"/> looks at the room
/// from outside and circles it.
/// </summary>
public enum CameraMode
{
    FirstPerson,
    Orbit,
}
