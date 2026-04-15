using System;
using System.Runtime.CompilerServices;

namespace Thea;

/// <summary>
/// Jump Consistent Hash 实现。
/// <para>
/// 参考资料：
/// <see href="https://arxiv.org/abs/1406.2294">A Fast, Minimal Memory, Consistent Hash Algorithm</see>
/// </para>
/// </summary>
public static class JumpConsistentHash
{
    /// <summary>
    /// 将 64 位哈希值映射到范围为 [0, <paramref name="bucketCount"/>) 的桶索引。
    /// </summary>
    /// <param name="key">64 位哈希后的键。</param>
    /// <param name="bucketCount">桶总数，必须大于 0。</param>
    /// <returns>桶索引。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetBucket(ulong key, int bucketCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bucketCount);

        long b = -1;
        long j = 0;

        while (j < bucketCount)
        {
            b = j;
            key = key * 2862933555777941757UL + 1;
            j = (long)((b + 1) * (double)(1L << 31) / (double)((key >> 33) + 1));
        }

        return (int)b;
    }

    /// <summary>
    /// 使用 <see cref="Farmhash"/> 对指定的字节跨度进行哈希，并映射到对应的桶。
    /// </summary>
    /// <param name="key">键的字节数据。</param>
    /// <param name="bucketCount">桶总数，必须大于 0。</param>
    /// <returns>桶索引。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetBucket(ReadOnlySpan<byte> key, int bucketCount)
        => GetBucket(Farmhash.Hash64(key), bucketCount);

    /// <summary>
    /// 使用 <see cref="Farmhash"/> 对指定的字节数组进行哈希，并映射到对应的桶。
    /// </summary>
    /// <param name="key">键的字节数据。</param>
    /// <param name="bucketCount">桶总数，必须大于 0。</param>
    /// <returns>桶索引。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetBucket(byte[] key, int bucketCount)
    {
        ArgumentNullException.ThrowIfNull(key);
        return GetBucket(Farmhash.Hash64(key, key.Length), bucketCount);
    }

    /// <summary>
    /// 使用 <see cref="Farmhash"/> 对指定的字符串进行哈希，并映射到对应的桶。
    /// </summary>
    /// <param name="key">字符串键。</param>
    /// <param name="bucketCount">桶总数，必须大于 0。</param>
    /// <returns>桶索引。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetBucket(string key, int bucketCount)
    {
        ArgumentNullException.ThrowIfNull(key);
        return GetBucket(Farmhash.Hash64(key), bucketCount);
    }
}