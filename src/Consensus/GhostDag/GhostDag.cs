using Krypteonx.Core.Config;
using Krypteonx.Core.Models;

namespace Krypteonx.Consensus.GhostDag;

public sealed class GhostDagStats
{
    public int BlocksProcessed { get; set; }
    public long FrontierUnionTotal { get; set; }
    public long FrontierMaximaTotal { get; set; }
    public long ParentExtrasTotal { get; set; }
    public long OpposingParentsTotal { get; set; }
    public long ExtraCandidatesTotal { get; set; }
    public long OrderedExtrasTotal { get; set; }
    public long OrderedDistinctTotal { get; set; }
    public long OrderedTruncations { get; set; }
    public long CandidateIdsTotal { get; set; }
    public long IncomparableWidthTotal { get; set; }
    public long RedTotal { get; set; }
    public int MaxIncomparableWidth { get; set; }
}

public sealed class GhostDag : IGhostDag
{
    private sealed class Node
    {
        public required Block Block { get; init; }
        public readonly List<Node> Parents = new();
        public readonly List<Node> Children = new();
        public Node? SelectedParent;
        public int BlueScore;
        public int Height;
        public int BluePrefix;
        public int MergesetBlueCount;
        public int MergesetRedCount;
        public readonly HashSet<string> MergesetBlues = new();
        public readonly HashSet<string> MergesetReds = new();
        public readonly HashSet<string> Ancestors = new();
        public readonly HashSet<string> Frontier = new();
        public readonly HashSet<string> OpposingParents = new();
    }

    private readonly Dictionary<string, Node> _nodes = new();
    private readonly HashSet<string> _tips = new();
    private readonly GhostDagStats _stats = new();

    private static bool IsAncestor(Node ancestor, Node node)
        => node.Ancestors.Contains(ancestor.Block.Id);

    private static List<Node> ReduceToMaxima(IEnumerable<Node> nodes)
    {
        var list = nodes.ToArray();
        var result = new List<Node>();
        foreach (var n in list)
        {
            var hasDescendant = list.Any(o => !ReferenceEquals(o, n) && o.Ancestors.Contains(n.Block.Id));
            if (!hasDescendant)
                result.Add(n);
        }
        return result;
    }

    public void AddBlock(Block block)
    {
        if (_nodes.ContainsKey(block.Id)) return;
        var node = new Node { Block = block };
        foreach (var pid in block.ParentIds)
        {
            if (!_nodes.TryGetValue(pid, out var p))
            {
                p = new Node { Block = new Block { Id = pid, ParentIds = Array.Empty<string>(), Timestamp = DateTime.UnixEpoch, Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } } };
                _nodes[pid] = p;
            }
            node.Parents.Add(p);
            p.Children.Add(node);
            _tips.Remove(pid);
            node.Ancestors.Add(pid);
            foreach (var aid in p.Ancestors)
                node.Ancestors.Add(aid);
        }

        foreach (var p in node.Parents)
        {
            foreach (var op in node.Parents)
            {
                if (op == p) continue;
                p.OpposingParents.Add(op.Block.Id);
            }
        }

        node.Height = node.Parents.Count == 0 ? 0 : node.Parents.Max(p => p.Height) + 1;

        node.SelectedParent = node.Parents.Count == 0 ? null : node.Parents
            .OrderByDescending(n => n.BlueScore)
            .ThenByDescending(n => n.Block.Timestamp)
            .First();

        node.BluePrefix = node.SelectedParent?.BlueScore ?? 0;

        var frontierIds = new HashSet<string>();
        foreach (var p in node.Parents)
        {
            frontierIds.Add(p.Block.Id);
            foreach (var fid in p.Frontier) frontierIds.Add(fid);
        }
        _stats.FrontierUnionTotal += frontierIds.Count;
        var frontierNodes = frontierIds
            .Select(id => _nodes.TryGetValue(id, out var n) ? n : null)
            .Where(n => n is not null)
            .Cast<Node>();
        var frontierMaxima = ReduceToMaxima(frontierNodes);
        _stats.FrontierMaximaTotal += frontierMaxima.Count;
        foreach (var fn in frontierMaxima)
            node.Frontier.Add(fn.Block.Id);

        var parentExtras = node.Parents.Where(p => p != node.SelectedParent).ToList();
        _stats.ParentExtrasTotal += parentExtras.Count;
        var extraCandidates = new List<Node>();
        if (node.SelectedParent is not null)
        {
            var candidateIds = new HashSet<string>();
            foreach (var op in parentExtras)
            {
                candidateIds.Add(op.Block.Id);
                foreach (var fid in op.Frontier) candidateIds.Add(fid);
            }
            foreach (var id in node.SelectedParent.OpposingParents)
                candidateIds.Add(id);
            _stats.OpposingParentsTotal += node.SelectedParent.OpposingParents.Count;
            candidateIds.Remove(node.SelectedParent.Block.Id);
            foreach (var sid in node.SelectedParent.Ancestors) candidateIds.Remove(sid);

            _stats.CandidateIdsTotal += candidateIds.Count;
            var candNodes = candidateIds
                .Select(id => _nodes.TryGetValue(id, out var n) ? n : null)
                .Where(n => n is not null)
                .Cast<Node>()
                .Where(n => n.Block.Timestamp >= node.Block.Timestamp - ChainParameters.GhostDagPastWindow)
                .ToArray();

            var maxima = ReduceToMaxima(candNodes);
            _stats.ExtraCandidatesTotal += maxima.Count;
            extraCandidates.AddRange(maxima);
        }
        var orderedDistinct = parentExtras
            .OrderByDescending(n => n.BlueScore)
            .ThenByDescending(n => n.Block.Timestamp)
            .Concat(
                extraCandidates
                    .OrderBy(n => node.Height - n.Height)
                    .ThenByDescending(n => n.BlueScore)
                    .ThenByDescending(n => n.Block.Timestamp)
            )
            .DistinctBy(n => n.Block.Id)
            .ToArray();
        _stats.OrderedDistinctTotal += orderedDistinct.Length;
        if (orderedDistinct.Length > ChainParameters.GhostDagFrontierMax) _stats.OrderedTruncations++;
        var orderedExtras = orderedDistinct
            .Take(ChainParameters.GhostDagFrontierMax)
            .ToArray();
        _stats.OrderedExtrasTotal += orderedExtras.Length;

        node.MergesetBlues.Add(node.Block.Id);
        var chosen = new List<Node>();
        var incomparable = new List<Node>();
        var chosenIds = new HashSet<string>();
        var chosenAncestorsUnion = new HashSet<string>();
        foreach (var p in orderedExtras)
        {
            var isComparableToAny = chosenAncestorsUnion.Contains(p.Block.Id) || p.Ancestors.Any(id => chosenIds.Contains(id));
            if (!isComparableToAny && incomparable.Count >= ChainParameters.GhostDagK)
            {
                node.MergesetReds.Add(p.Block.Id);
                _stats.RedTotal++;
                continue;
            }

            chosen.Add(p);
            chosenIds.Add(p.Block.Id);
            foreach (var aid in p.Ancestors) chosenAncestorsUnion.Add(aid);
            if (!isComparableToAny)
                incomparable.Add(p);
            node.MergesetBlues.Add(p.Block.Id);
        }
        _stats.IncomparableWidthTotal += incomparable.Count;
        if (incomparable.Count > _stats.MaxIncomparableWidth) _stats.MaxIncomparableWidth = incomparable.Count;

        node.MergesetBlueCount = node.MergesetBlues.Count;
        node.MergesetRedCount = node.MergesetReds.Count;
        node.BlueScore = (node.SelectedParent?.BlueScore ?? 0) + node.MergesetBlueCount;

        _nodes[block.Id] = node;
        _tips.Add(block.Id);
        _stats.BlocksProcessed++;
    }

    public IReadOnlyList<Block> OrderBlocks(IReadOnlyList<Block> parallelBlocks)
    {
        foreach (var b in parallelBlocks) AddBlock(b);
        return parallelBlocks
            .OrderByDescending(b => _nodes.TryGetValue(b.Id, out var n) ? n.BlueScore : 0)
            .ThenBy(b => b.Timestamp)
            .ToArray();
    }

    public IReadOnlyList<string> GetTips() => _tips.OrderBy(id => id).ToArray();

    public int GetBlueScore(string blockId) => _nodes.TryGetValue(blockId, out var n) ? n.BlueScore : 0;

    public string? GetSelectedParent(string blockId)
    {
        if (!_nodes.TryGetValue(blockId, out var n)) return null;
        return n.SelectedParent?.Block.Id;
    }

    public int GetMergesetBlueCount(string blockId)
        => _nodes.TryGetValue(blockId, out var n) ? n.MergesetBlueCount : 0;

    public int GetMergesetRedCount(string blockId)
        => _nodes.TryGetValue(blockId, out var n) ? n.MergesetRedCount : 0;

    public IReadOnlyList<string> GetMergesetBlues(string blockId)
        => _nodes.TryGetValue(blockId, out var n) ? n.MergesetBlues.OrderBy(id => id).ToArray() : Array.Empty<string>();

    public IReadOnlyList<string> GetMergesetReds(string blockId)
        => _nodes.TryGetValue(blockId, out var n) ? n.MergesetReds.OrderBy(id => id).ToArray() : Array.Empty<string>();

    public string? GetHeavyTip()
    {
        if (_tips.Count == 0) return null;
        return _tips
            .Select(id => _nodes[id])
            .OrderByDescending(n => n.BlueScore)
            .ThenByDescending(n => n.Block.Timestamp)
            .First().Block.Id;
    }

    public IReadOnlyList<string> GetSelectedChain(string blockId, int limit)
    {
        if (!_nodes.TryGetValue(blockId, out var n)) return Array.Empty<string>();
        var list = new List<string>();
        var cur = n;
        while (cur is not null && list.Count < limit)
        {
            list.Add(cur.Block.Id);
            cur = cur.SelectedParent;
        }
        list.Reverse();
        return list;
    }

    public GhostDagStats GetStats() => new GhostDagStats
    {
        BlocksProcessed = _stats.BlocksProcessed,
        FrontierUnionTotal = _stats.FrontierUnionTotal,
        FrontierMaximaTotal = _stats.FrontierMaximaTotal,
        ParentExtrasTotal = _stats.ParentExtrasTotal,
        OpposingParentsTotal = _stats.OpposingParentsTotal,
        ExtraCandidatesTotal = _stats.ExtraCandidatesTotal,
        OrderedExtrasTotal = _stats.OrderedExtrasTotal,
        OrderedDistinctTotal = _stats.OrderedDistinctTotal,
        OrderedTruncations = _stats.OrderedTruncations,
        CandidateIdsTotal = _stats.CandidateIdsTotal,
        IncomparableWidthTotal = _stats.IncomparableWidthTotal,
        RedTotal = _stats.RedTotal,
        MaxIncomparableWidth = _stats.MaxIncomparableWidth,
    };
}
