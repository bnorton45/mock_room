using System.Text.Json.Serialization;

namespace MockRoom.Core.Persistence;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for <see cref="RoomDocument"/>.
/// Source generation (not reflection) keeps JSON save/load NativeAOT- and trim-safe;
/// <c>UseStringEnumConverter</c> writes enums by name for readable, stable files.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(RoomDocument))]
internal sealed partial class RoomJsonContext : JsonSerializerContext;
