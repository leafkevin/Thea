using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Thea;

/// <summary>
/// Rendezvous Hash（最高随机权重哈希）实现。
/// </summary>
/// <typeparam name="TNode">节点类型。</typeparam>
public sealed class RendezvousHash<TNode>
{
    private readonly Entry[] entries;

    private readonly struct Entry
    {
        public readonly TNode Node;
        public readonly ulong NodeHash;

        public Entry(TNode node, ulong nodeHash)
        {
            this.Node = node;
            this.NodeHash = nodeHash;
        }
    }

    private readonly struct ScoredEntry
    {
        public readonly TNode Node;
        public readonly ulong NodeHash;
        public readonly ulong Score;

        public ScoredEntry(TNode node, ulong nodeHash, ulong score)
        {
            this.Node = node;
            this.NodeHash = nodeHash;
            this.Score = score;
        }
    }

    /// <summary>
    /// 使用节点字符串键选择器初始化 Rendezvous Hash。
    /// </summary>
    /// <param name="nodes">可用节点集合。</param>
    /// <param name="nodeKeySelector">用于提取稳定节点键的选择器。</param>
    public RendezvousHash(IEnumerable<TNode> nodes, Func<TNode, string> nodeKeySelector)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(nodeKeySelector);

        var list = new List<Entry>();
        foreach (var node in nodes)
        {
            var nodeKey = nodeKeySelector(node)
                ?? throw new ArgumentException("Node key selector returned null.", nameof(nodeKeySelector));
            list.Add(new Entry(node, Farmhash.Hash64(nodeKey)));
        }
        this.entries = [.. list];
    }

    /// <summary>
    /// 使用预先计算好的节点哈希选择器初始化 Rendezvous Hash。
    /// </summary>
    /// <param name="nodes">可用节点集合。</param>
    /// <param name="nodeHashSelector">用于提取稳定 64 位节点哈希的选择器。</param>
    public RendezvousHash(IEnumerable<TNode> nodes, Func<TNode, ulong> nodeHashSelector)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(nodeHashSelector);

        var list = new List<Entry>();
        foreach (var node in nodes)
        {
            list.Add(new Entry(node, nodeHashSelector(node)));
        }
        this.entries = [.. list];
    }

    /// <summary>
    /// 获取当前配置的节点数量。
    /// </summary>
    public int Count => this.entries.Length;

    /// <summary>
    /// 将字符串键路由到一个节点。
    /// </summary>
    /// <param name="key">路由键。</param>
    /// <returns>选中的节点。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TNode GetNode(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return this.GetNode(Farmhash.Hash64(key));
    }

    /// <summary>
    /// 将字节键路由到一个节点。
    /// </summary>
    /// <param name="key">路由键。</param>
    /// <returns>选中的节点。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TNode GetNode(ReadOnlySpan<byte> key)
        => this.GetNode(Farmhash.Hash64(key));

    /// <summary>
    /// 将预先计算好的 64 位键哈希路由到一个节点。
    /// </summary>
    /// <param name="keyHash">预先计算好的 64 位键哈希。</param>
    /// <returns>选中的节点。</returns>
    public TNode GetNode(ulong keyHash)
    {
        if (!this.TryGetNode(keyHash, out var node))
            throw new InvalidOperationException("No nodes are configured.");

        return node;
    }

    /// <summary>
    /// 尝试将字符串键路由到一个节点。
    /// </summary>
    /// <param name="key">路由键。</param>
    /// <param name="node">成功时返回选中的节点。</param>
    /// <returns>如果存在可用节点则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetNode(string key, out TNode node)
    {
        ArgumentNullException.ThrowIfNull(key);
        return this.TryGetNode(Farmhash.Hash64(key), out node);
    }

    /// <summary>
    /// 尝试将字节键路由到一个节点。
    /// </summary>
    /// <param name="key">路由键。</param>
    /// <param name="node">成功时返回选中的节点。</param>
    /// <returns>如果存在可用节点则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetNode(ReadOnlySpan<byte> key, out TNode node)
        => this.TryGetNode(Farmhash.Hash64(key), out node);

    /// <summary>
    /// 尝试将预先计算好的 64 位键哈希路由到一个节点。
    /// </summary>
    /// <param name="keyHash">预先计算好的 64 位键哈希。</param>
    /// <param name="node">成功时返回选中的节点。</param>
    /// <returns>如果存在可用节点则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
    public bool TryGetNode(ulong keyHash, out TNode node)
    {
        if (this.entries.Length == 0)
        {
            node = default!;
            return false;
        }

        var bestIndex = 0;
        var bestScore = ComputeScore(keyHash, this.entries[0].NodeHash);
        var bestNodeHash = this.entries[0].NodeHash;

        for (var i = 1; i < this.entries.Length; i++)
        {
            var entry = this.entries[i];
            var score = ComputeScore(keyHash, entry.NodeHash);
            if (score > bestScore || (score == bestScore && entry.NodeHash > bestNodeHash))
            {
                bestIndex = i;
                bestScore = score;
                bestNodeHash = entry.NodeHash;
            }
        }

        node = this.entries[bestIndex].Node;
        return true;
    }

    /// <summary>
    /// 获取字符串键对应得分最高的前几个节点。
    /// </summary>
    /// <param name="key">路由键。</param>
    /// <param name="count">要返回的节点数量。</param>
    /// <returns>按得分从高到低排序的节点集合。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TNode[] GetTopNodes(string key, int count)
    {
        ArgumentNullException.ThrowIfNull(key);
        return this.GetTopNodes(Farmhash.Hash64(key), count);
    }

    /// <summary>
    /// 获取字节键对应得分最高的前几个节点。
    /// </summary>
    /// <param name="key">路由键。</param>
    /// <param name="count">要返回的节点数量。</param>
    /// <returns>按得分从高到低排序的节点集合。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TNode[] GetTopNodes(ReadOnlySpan<byte> key, int count)
        => this.GetTopNodes(Farmhash.Hash64(key), count);

    /// <summary>
    /// 获取预先计算好的 64 位键哈希对应得分最高的前几个节点。
    /// </summary>
    /// <param name="keyHash">预先计算好的 64 位键哈希。</param>
    /// <param name="count">要返回的节点数量。</param>
    /// <returns>按得分从高到低排序的节点集合。</returns>
    public TNode[] GetTopNodes(ulong keyHash, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        if (this.entries.Length == 0)
            return [];

        if (count > this.entries.Length)
            count = this.entries.Length;

        var scored = new ScoredEntry[this.entries.Length];
        for (var i = 0; i < this.entries.Length; i++)
        {
            var entry = this.entries[i];
            scored[i] = new ScoredEntry(entry.Node, entry.NodeHash, ComputeScore(keyHash, entry.NodeHash));
        }

        Array.Sort(scored, static (x, y) =>
        {
            var scoreCompare = y.Score.CompareTo(x.Score);
            if (scoreCompare != 0)
                return scoreCompare;
            return y.NodeHash.CompareTo(x.NodeHash);
        });

        var result = new TNode[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = scored[i].Node;
        }
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ComputeScore(ulong keyHash, ulong nodeHash)
    {
        Span<byte> buffer = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, keyHash);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(8), nodeHash);
        return Farmhash.Hash64(buffer);
    }
}