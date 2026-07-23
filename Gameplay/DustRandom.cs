using System.Security.Cryptography;

namespace Dust;

/// <summary>
/// Small deterministic generator whose state can travel in an online checkpoint.
/// System.Random intentionally hides its internal state, which makes safe host
/// migration impossible after the original simulation host disconnects.
/// </summary>
internal sealed class DustRandom : Random
{
    private ulong _state;

    public DustRandom() : this(CreateSeed())
    {
    }

    public DustRandom(long seed) : this(unchecked((ulong)seed))
    {
    }

    public DustRandom(ulong seed)
    {
        // SplitMix64 accepts zero, but scrambling the caller's seed first keeps
        // small, adjacent run seeds from beginning with visibly related output.
        _state = seed + 0x9E3779B97F4A7C15UL;
        _ = NextUInt64();
    }

    public ulong State
    {
        get => _state;
        set => _state = value;
    }

    public override int Next() => (int)(NextUInt64() % int.MaxValue);

    public override int Next(int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return maxValue == 0 ? 0 : (int)NextBounded((uint)maxValue);
    }

    public override int Next(int minValue, int maxValue)
    {
        if (minValue > maxValue) throw new ArgumentOutOfRangeException(nameof(minValue));
        var range = (long)maxValue - minValue;
        if (range == 0) return minValue;
        return (int)(minValue + (long)NextBounded((ulong)range));
    }

    public override long NextInt64() => (long)(NextUInt64() & long.MaxValue);

    public override long NextInt64(long maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return maxValue == 0 ? 0 : (long)NextBounded((ulong)maxValue);
    }

    public override long NextInt64(long minValue, long maxValue)
    {
        if (minValue > maxValue) throw new ArgumentOutOfRangeException(nameof(minValue));
        var range = unchecked((ulong)(maxValue - minValue));
        if (range == 0) return minValue;
        return minValue + (long)NextBounded(range);
    }

    public override double NextDouble() =>
        (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public override void NextBytes(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        NextBytes(buffer.AsSpan());
    }

    public override void NextBytes(Span<byte> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var value = NextUInt64();
            for (var index = 0; index < sizeof(ulong) && offset < buffer.Length; index++, offset++)
                buffer[offset] = (byte)(value >> (index * 8));
        }
    }

    protected override double Sample() => NextDouble();

    private ulong NextUInt64()
    {
        // SplitMix64 is compact, deterministic across runtimes, and has exactly
        // one exportable 64-bit state value.
        var value = _state += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    private ulong NextBounded(ulong bound)
    {
        if (bound == 0) return 0;
        var threshold = unchecked(0UL - bound) % bound;
        while (true)
        {
            var value = NextUInt64();
            if (value >= threshold) return value % bound;
        }
    }

    private static ulong CreateSeed()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes);
    }
}
