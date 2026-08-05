//--------------------------//
//--------定义 Web Terminal HTTP 与 WebSocket 控制契约---------//
//--------Defines Web Terminal HTTP and WebSocket control contracts--------//
//-------------------------//
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Nas.Api.Contracts;

public sealed record CreateTerminalSessionRequest(
    [param: Required, MaxLength(256)] string Password,
    [param: Range(20, 300)] ushort Columns = 120,
    [param: Range(5, 120)] ushort Rows = 32);

public sealed record CreateTerminalSessionResponse(
    Guid SessionId,
    DateTimeOffset ExpiresAt,
    string WebSocketPath);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TerminalResizeMessage), "resize")]
[JsonDerivedType(typeof(TerminalCloseMessage), "close")]
public abstract record TerminalClientControlMessage;

public sealed record TerminalResizeMessage(
    [property: Range(20, 300)] ushort Columns,
    [property: Range(5, 120)] ushort Rows) : TerminalClientControlMessage;

public sealed record TerminalCloseMessage : TerminalClientControlMessage;

public sealed record TerminalServerControlMessage(
    string Type,
    int? ExitCode = null,
    string? Code = null,
    string? Message = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TerminalClientControlMessage))]
[JsonSerializable(typeof(TerminalServerControlMessage))]
public sealed partial class TerminalApiJsonContext : JsonSerializerContext;
