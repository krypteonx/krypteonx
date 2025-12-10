using Krypteonx.Core.Models;
using Krypteonx.Consensus.GhostDag;
using Xunit;

namespace Krypteonx.Tests;

public class AncestorCacheTests
{
    [Fact]
    public void TransitiveAncestors_AreCached()
    {
        var dag = new GhostDag();
        var g = new Block { Id = "G", ParentIds = Array.Empty<string>(), Timestamp = DateTime.UtcNow.AddSeconds(-50), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(g);
        var a = new Block { Id = "A", ParentIds = new[] { "G" }, Timestamp = DateTime.UtcNow.AddSeconds(-40), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(a);
        var b = new Block { Id = "B", ParentIds = new[] { "A" }, Timestamp = DateTime.UtcNow.AddSeconds(-30), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(b);
        var c = new Block { Id = "C", ParentIds = new[] { "B" }, Timestamp = DateTime.UtcNow.AddSeconds(-20), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(c);

        var chain = dag.GetSelectedChain("C", 10);
        Assert.Equal(new[] { "G", "A", "B", "C" }, chain);
    }
}

