using Krypteonx.Core.Models;
using Krypteonx.Consensus.GhostDag;
using Xunit;

namespace Krypteonx.Tests;

public class PastFrontierTests
{
    [Fact]
    public void PastDiffFrontier_PicksMaximalAncestorsOnly()
    {
        var dag = new GhostDag();
        var g = new Block { Id = "G", ParentIds = Array.Empty<string>(), Timestamp = DateTime.UtcNow.AddSeconds(-60), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(g);
        var x = new Block { Id = "X", ParentIds = new[] { "G" }, Timestamp = DateTime.UtcNow.AddSeconds(-50), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        var y = new Block { Id = "Y", ParentIds = new[] { "G" }, Timestamp = DateTime.UtcNow.AddSeconds(-49), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(x);
        dag.AddBlock(y);

        var a = new Block { Id = "A", ParentIds = new[] { "X" }, Timestamp = DateTime.UtcNow.AddSeconds(-40), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        var b = new Block { Id = "B", ParentIds = new[] { "Y" }, Timestamp = DateTime.UtcNow.AddSeconds(-39), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(a);
        dag.AddBlock(b);

        var c = new Block { Id = "C", ParentIds = new[] { "A", "B" }, Timestamp = DateTime.UtcNow.AddSeconds(-20), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(c);

        var d = new Block { Id = "D", ParentIds = new[] { "A" }, Timestamp = DateTime.UtcNow.AddSeconds(-10), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(d);

        var bluesD = dag.GetMergesetBlues("D");
        Assert.Contains("D", bluesD);
        Assert.Contains("B", bluesD); // Y-branch maximal ancestor from past diff (since selected parent likely A)
        Assert.DoesNotContain("Y", bluesD); // non-maximal ancestor should not be chosen
        Assert.DoesNotContain("G", bluesD); // deep ancestor excluded by frontier filter
    }
}

