using Krypteonx.Core.Models;

namespace Krypteonx.Consensus.AuxPoW;

public sealed class MoneroAuxPowBridge : IAuxPowBridge
{
    public bool TryValidateAuxPow(Block block)
    {
        return block.Header.PowData.Length > 0;
    }
}

