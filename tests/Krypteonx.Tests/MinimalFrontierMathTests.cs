using Krypteonx.Core.Models;
using Krypteonx.Consensus.GhostDag;
using Xunit;

namespace Krypteonx.Tests;

public class MinimalFrontierMathTests
{
    [Fact]
    public void DiffFrontier_PicksOnlyMaxima_NotAncestors()
    {
        var dag = new GhostDag();
        var g = new Block { Id = "G", ParentIds = Array.Empty<string>(), Timestamp = DateTime.UtcNow.AddSeconds(-120), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(g);

        var x = new Block { Id = "X", ParentIds = new[] { "G" }, Timestamp = DateTime.UtcNow.AddSeconds(-110), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        var y = new Block { Id = "Y", ParentIds = new[] { "G" }, Timestamp = DateTime.UtcNow.AddSeconds(-109), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(x);
        dag.AddBlock(y);

        var a = new Block { Id = "A", ParentIds = new[] { "X" }, Timestamp = DateTime.UtcNow.AddSeconds(-90), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        var b = new Block { Id = "B", ParentIds = new[] { "Y" }, Timestamp = DateTime.UtcNow.AddSeconds(-89), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        var b2 = new Block { Id = "B2", ParentIds = new[] { "B" }, Timestamp = DateTime.UtcNow.AddSeconds(-70), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(a);
        dag.AddBlock(b);
        dag.AddBlock(b2);

        var c = new Block { Id = "C", ParentIds = new[] { "A" }, Timestamp = DateTime.UtcNow.AddSeconds(-10), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(c);

        var d = new Block { Id = "D", ParentIds = new[] { "C", "B2" }, Timestamp = DateTime.UtcNow, Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(d);

        var blues = dag.GetMergesetBlues("D");
        Assert.Contains("D", blues);
        Assert.Contains("B2", blues); // maximal in diff
        Assert.DoesNotContain("B", blues); // non-maximal ancestor
        Assert.DoesNotContain("Y", blues); // deeper non-maximal ancestor
    }
}

