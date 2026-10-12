using System.Buffers.Binary;
using System.Text.Json;

namespace ReMinder.Domain;

public sealed record WireMessage
{
    public string Kind { get; init; } = "";
    public int ProtocolVersion { get; init; } = 1;
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");
    public string Token { get; init; } = "";
    public string Action { get; init; } = "";
    public string TaskId { get; init; } = "";
    public DateTimeOffset? ExpectedVersion { get; init; }
    public string Text { get; init; } = "";
    public string Mode { get; init; } = "";
    public bool CanUndo { get; init; }
    public AppState? State { get; init; }
}

public static class Wire
{
    public const int MaxLength = 16 * 1024 * 1024;
    public static async Task WriteAsync(Stream stream, WireMessage value, SemaphoreSlim writeGate, CancellationToken token = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Serialization.Json);
        if (bytes.Length > MaxLength) throw new InvalidDataException("消息超过允许大小。");
        var length = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        await writeGate.WaitAsync(token);
        try { await stream.WriteAsync(length, token); await stream.WriteAsync(bytes, token); await stream.FlushAsync(token); }
        finally { writeGate.Release(); }
    }
    public static async Task<WireMessage> ReadAsync(Stream stream, CancellationToken token = default)
    {
        var size = new byte[4]; await stream.ReadExactlyAsync(size, token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(size);
        if (length is < 1 or > MaxLength) throw new InvalidDataException("消息长度无效。");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, token);
        var msg = JsonSerializer.Deserialize<WireMessage>(bytes, Serialization.Json) ?? throw new InvalidDataException("消息为空。");
        if (msg.ProtocolVersion != 1) throw new InvalidDataException("组件版本不匹配。");
        return msg;
    }
}
