using System.Text.Json;
using System.Text.Json.Serialization;

namespace LexCore.Client;

/// <summary>
/// Source-generated JSON metadata for every type the client serializes or
/// deserializes. Source generation (not reflection) keeps the client trim- and
/// NativeAOT-safe when it is compiled into a published app. <see cref="JsonSerializerDefaults.Web"/>
/// preserves the existing wire format: camelCase property names and case-insensitive
/// reads, matching what the anonymous-object payloads and the HTTP JSON helpers used before.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ActivateRequest))]
[JsonSerializable(typeof(TrialStartRequest))]
[JsonSerializable(typeof(ActivateResponse))]
[JsonSerializable(typeof(ValidateResponse))]
[JsonSerializable(typeof(TrialResponse))]
[JsonSerializable(typeof(ActivationState))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class LexCoreJsonContext : JsonSerializerContext;
