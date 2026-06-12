namespace MockRoom.Core.Rendering;

/// <summary>
/// A triangle-list mesh ready to upload to the GPU. <see cref="Vertices"/> is an
/// interleaved buffer of <see cref="RoomMeshBuilder.FloatsPerVertex"/> floats per
/// vertex (position xyz, normal xyz, color rgb). No GL types so the domain library
/// stays renderer-agnostic and NativeAOT-clean.
/// </summary>
public readonly record struct MeshData(float[] Vertices, int VertexCount);
