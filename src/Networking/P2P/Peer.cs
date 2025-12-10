using LiteNetLib;
using Krypteonx.Networking.P2P;
using static Krypteonx.Networking.P2P.P2PServer;

namespace Krypteonx.Networking.P2P;

public sealed class Peer : IPeer
{
    private readonly NetPeer _peer;
    private readonly string _id;

    public Peer(NetPeer peer)
    {
        _peer = peer;
        _id = $"udp://{peer}";
    }

    public string Id => _id;

    public void Send(ReadOnlySpan<byte> message)
    {
        _peer.Send(message.ToArray(), DeliveryMethod.ReliableOrdered);
    }

    public void SendEnvelope(Envelope env)
    {
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(env);
        var method = DeliveryMethod.ReliableOrdered;
        switch (env.Kind)
        {
            case MessageKind.Heartbeat:
            case MessageKind.HeartbeatAck:
                method = DeliveryMethod.Sequenced;
                break;
            case MessageKind.Block:
            case MessageKind.Transaction:
            case MessageKind.Handshake:
            case MessageKind.HandshakeAck:
                method = DeliveryMethod.ReliableOrdered;
                break;
        }
        _peer.Send(bytes, method);
    }

    public void SendEnvelopeWithReliability(Envelope env, bool reliable)
    {
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(env);
        var method = DeliveryMethod.ReliableOrdered;
        if (env.Kind == MessageKind.Heartbeat || env.Kind == MessageKind.HeartbeatAck)
        {
            method = reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced;
        }
        _peer.Send(bytes, method);
    }
}
