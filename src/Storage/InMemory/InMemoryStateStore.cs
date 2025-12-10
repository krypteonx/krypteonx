using Krypteonx.Storage.Abstractions;

namespace Krypteonx.Storage.InMemory;

public sealed class InMemoryStateStore : IStateStore
{
    private readonly Dictionary<string, byte[]> _kv = new();

    public byte[]? Get(string key) => _kv.TryGetValue(key, out var v) ? v : null;
    public void Put(string key, ReadOnlySpan<byte> value) => _kv[key] = value.ToArray();
}

