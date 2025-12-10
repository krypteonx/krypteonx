using Krypteonx.Core.Models;
using Krypteonx.Consensus.GhostDag;
using Xunit;

namespace Krypteonx.Tests;

public class GhostDagTests
{
    [Fact]
    public void SelectedParent_IsHighestBlueScore()
    {
        var dag = new GhostDag();
        var genesis = new Block { Id = "G", ParentIds = Array.Empty<string>(), Timestamp = DateTime.UtcNow.AddSeconds(-10), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(genesis);

        var a = new Block { Id = "A", ParentIds = new[] { "G" }, Timestamp = DateTime.UtcNow.AddSeconds(-5), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        var b = new Block { Id = "B", ParentIds = new[] { "G" }, Timestamp = DateTime.UtcNow.AddSeconds(-4), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(a);
        dag.AddBlock(b);

        var c = new Block { Id = "C", ParentIds = new[] { "A", "B" }, Timestamp = DateTime.UtcNow, Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(c);

        var sp = dag.GetSelectedParent("C");
        Assert.Equal("B", sp);

        var bsC = dag.GetBlueScore("C");
        var bsB = dag.GetBlueScore("B");
        Assert.True(bsC > bsB);
    }

    [Fact]
    public void Tips_UpdateWhenAddingBlocks()
    {
        var dag = new GhostDag();
        var g = new Block { Id = "G", ParentIds = Array.Empty<string>(), Timestamp = DateTime.UtcNow.AddSeconds(-10), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(g);
        var a = new Block { Id = "A", ParentIds = new[] { "G" }, Timestamp = DateTime.UtcNow.AddSeconds(-5), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        var b = new Block { Id = "B", ParentIds = new[] { "G" }, Timestamp = DateTime.UtcNow.AddSeconds(-4), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(a);
        dag.AddBlock(b);
        var tips1 = dag.GetTips();
        Assert.Contains("A", tips1);
        Assert.Contains("B", tips1);
        Assert.DoesNotContain("G", tips1);

        var c = new Block { Id = "C", ParentIds = new[] { "A" }, Timestamp = DateTime.UtcNow, Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(c);
        var tips2 = dag.GetTips();
        Assert.Contains("B", tips2);
        Assert.Contains("C", tips2);
        Assert.DoesNotContain("A", tips2);
    }

    [Fact]
    public void Mergeset_Respects_K_Limit()
    {
        var dag = new GhostDag();
        var g = new Block { Id = "G", ParentIds = Array.Empty<string>(), Timestamp = DateTime.UtcNow.AddSeconds(-20), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(g);

        var parents = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            var id = $"P{i}";
            var p = new Block { Id = id, ParentIds = new[] { "G" }, Timestamp = DateTime.UtcNow.AddSeconds(-10 + i), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
            dag.AddBlock(p);
            parents.Add(id);
        }

        var c = new Block { Id = "C", ParentIds = parents.ToArray(), Timestamp = DateTime.UtcNow, Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(c);

        var blue = dag.GetMergesetBlueCount("C");
        var red = dag.GetMergesetRedCount("C");

        Assert.Equal(9, blue); // 1 + min(K=8, extras=9) => 1 + 8 = 9
        Assert.Equal(1, red);  // extras(9) - 8 = 1

        var bs = dag.GetBlueScore("C");
        Assert.Equal(11, bs); // selectedParent(BlueScore=2) + mergesetBlueCount(9)

        var blues = dag.GetMergesetBlues("C");
        var reds = dag.GetMergesetReds("C");
        Assert.Contains("C", blues);
        Assert.Contains("P8", blues);
        Assert.Contains("P2", blues);
        Assert.Contains("P1", blues);
        Assert.Contains("P0", reds);
    }

    [Fact]
    public void HeavyTip_And_SelectedChain_Work()
    {
        var dag = new GhostDag();
        var g = new Block { Id = "G", ParentIds = Array.Empty<string>(), Timestamp = DateTime.UtcNow.AddSeconds(-40), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(g);
        var a = new Block { Id = "A", ParentIds = new[] { "G" }, Timestamp = DateTime.UtcNow.AddSeconds(-30), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        var b = new Block { Id = "B", ParentIds = new[] { "A" }, Timestamp = DateTime.UtcNow.AddSeconds(-20), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        var c = new Block { Id = "C", ParentIds = new[] { "B" }, Timestamp = DateTime.UtcNow.AddSeconds(-10), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(a);
        dag.AddBlock(b);
        dag.AddBlock(c);

        var tip = dag.GetHeavyTip();
        Assert.Equal("C", tip);

        var chain = dag.GetSelectedChain("C", 10);
        Assert.Equal(new[] { "G", "A", "B", "C" }, chain);

        var bsG = dag.GetBlueScore("G");
        var bsA = dag.GetBlueScore("A");
        var bsB = dag.GetBlueScore("B");
        var bsC = dag.GetBlueScore("C");
        Assert.True(bsG < bsA && bsA < bsB && bsB < bsC);
    }

    [Fact]
    public void AncestorParents_DoNotConflict_InMergeset()
    {
        var dag = new GhostDag();
        var g = new Block { Id = "G", ParentIds = Array.Empty<string>(), Timestamp = DateTime.UtcNow.AddSeconds(-50), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(g);
        var a = new Block { Id = "A", ParentIds = new[] { "G" }, Timestamp = DateTime.UtcNow.AddSeconds(-40), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        var b = new Block { Id = "B", ParentIds = new[] { "A" }, Timestamp = DateTime.UtcNow.AddSeconds(-30), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(a);
        dag.AddBlock(b);

        var c = new Block { Id = "C", ParentIds = new[] { "B", "A" }, Timestamp = DateTime.UtcNow.AddSeconds(-20), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(c);

        var blue = dag.GetMergesetBlueCount("C");
        var red = dag.GetMergesetRedCount("C");
        Assert.Equal(2, blue);
        Assert.Equal(0, red);

        var blues = dag.GetMergesetBlues("C");
        Assert.Contains("C", blues);
        Assert.Contains("A", blues);
    }

    [Fact]
    public void PastWindow_Filters_DistantAncestors()
    {
        var dag = new GhostDag();
        var g = new Block { Id = "G", ParentIds = Array.Empty<string>(), Timestamp = DateTime.UtcNow.AddSeconds(-300), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(g);

        var a = new Block { Id = "A", ParentIds = new[] { "G" }, Timestamp = DateTime.UtcNow.AddSeconds(-200), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        var a2old = new Block { Id = "A2O", ParentIds = new[] { "G" }, Timestamp = DateTime.UtcNow.AddSeconds(-250), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        var a2 = new Block { Id = "A2", ParentIds = new[] { "A2O" }, Timestamp = DateTime.UtcNow.AddSeconds(-95), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(a);
        dag.AddBlock(a2old);
        dag.AddBlock(a2);

        var b = new Block { Id = "B", ParentIds = new[] { "A" }, Timestamp = DateTime.UtcNow.AddSeconds(-20), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(b);

        var c = new Block { Id = "C", ParentIds = new[] { "B", "A2" }, Timestamp = DateTime.UtcNow, Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(c);

        var blues = dag.GetMergesetBlues("C");
        Assert.Contains("C", blues);
        Assert.Contains("A2", blues);
        Assert.DoesNotContain("A2O", blues); // filtered by past window
    }
}
