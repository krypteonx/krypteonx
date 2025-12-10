namespace Krypteonx.Networking.P2P;

public interface IPeer
{
    string Id { get; }
    void Send(ReadOnlySpan<byte> message);
}

