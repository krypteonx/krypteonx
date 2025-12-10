using Krypteonx.Core.Models;

namespace Krypteonx.Storage.Abstractions;

public interface IBlockStore
{
    void Put(Block block);
    Block? Get(string id);
}

