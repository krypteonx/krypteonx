namespace Krypteonx.Core.Models;

public sealed class Block
{
    public required string Id { get; init; }
    public required IReadOnlyList<string> ParentIds { get; init; }
    public required DateTime Timestamp { get; init; }
    public required IReadOnlyList<Transaction> Transactions { get; init; }
    public required BlockHeader Header { get; init; }
}

public sealed class BlockHeader
{
    public required string MerkleRoot { get; init; }
    public required byte[] PowData { get; init; }
}

