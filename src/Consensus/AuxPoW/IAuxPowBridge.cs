using Krypteonx.Core.Models;

namespace Krypteonx.Consensus.AuxPoW;

public interface IAuxPowBridge
{
    bool TryValidateAuxPow(Block block);
}

