using Krypteonx.Core.Ledger;

namespace Krypteonx.Execution.DualState;

public sealed class DualStateLedger
{
    public PublicState Public { get; } = new();
    public PrivateState Private { get; } = new();
}

