using Krypteonx.Core.Models;
using Krypteonx.Storage.Abstractions;

namespace Krypteonx.Storage.InMemory;

public sealed class InMemoryBlockStore : IBlockStore
{
    private readonly Dictionary<string, Block> _blocks = new();

    public Block? Get(string id) => _blocks.TryGetValue(id, out var b) ? b : null;
    public void Put(Block block) => _blocks[block.Id] = block;
}

