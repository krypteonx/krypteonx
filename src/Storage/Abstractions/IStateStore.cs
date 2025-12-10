namespace Krypteonx.Storage.Abstractions;

public interface IStateStore
{
    byte[]? Get(string key);
    void Put(string key, ReadOnlySpan<byte> value);
}

