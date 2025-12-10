namespace Krypteonx.Core.Models;

public sealed class Transaction
{
    public required string Id { get; init; }
    public required TransactionKind Kind { get; init; }
    public required byte[] Payload { get; init; }
}

public enum TransactionKind
{
    Public,
    Private
}

