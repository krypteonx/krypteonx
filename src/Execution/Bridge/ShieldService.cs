using Krypteonx.Core.Ledger;

namespace Krypteonx.Execution.Bridge;

public sealed class ShieldService
{
    private readonly PublicState _public;
    private readonly PrivateState _private;

    public ShieldService(PublicState publicState, PrivateState privateState)
    {
        _public = publicState;
        _private = privateState;
    }

    public void Shield(string tAddress, long amount, string nullifier)
    {
        var bal = _public.GetBalance(tAddress);
        if (bal < amount) throw new InvalidOperationException("insufficient balance");
        _public.SetBalance(tAddress, bal - amount);
        _private.MarkSpent(nullifier);
    }

    public void Unshield(string tAddress, long amount)
    {
        var bal = _public.GetBalance(tAddress);
        _public.SetBalance(tAddress, bal + amount);
    }
}

