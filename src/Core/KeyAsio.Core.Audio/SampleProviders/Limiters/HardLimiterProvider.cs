using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using NAudio.Wave;

namespace KeyAsio.Core.Audio.SampleProviders.Limiters;

/// <summary>
/// A hard clipping limiter that chops off signals exceeding the ceiling.
/// SIMD-accelerated via vectorized Min/Max.
/// </summary>
public sealed class HardLimiterProvider : LimiterBase
{
    private float _ceiling = 1.0f;

    public HardLimiterProvider(ISampleProvider source, float ceiling = 0.99f) : base(source)
    {
        Ceiling = ceiling;
    }

    public float Ceiling
    {
        get => _ceiling;
        set => _ceiling = Math.Clamp(value, 0.1f, 1.0f);
    }

    protected override void Process(float[] buffer, int offset, int count)
    {
        int i = 0;
        ref float dataRef = ref buffer[offset];

        var vCeil = Vector256.Create(_ceiling);
        var vNegCeil = Vector256.Create(-_ceiling);
        int limit256 = count - Vector256<float>.Count;

        for (; i <= limit256; i += Vector256<float>.Count)
        {
            var v = Vector256.LoadUnsafe(ref Unsafe.Add(ref dataRef, i));
            v = Vector256.Min(Vector256.Max(v, vNegCeil), vCeil);
            v.StoreUnsafe(ref Unsafe.Add(ref dataRef, i));
        }

        var vCeil128 = Vector128.Create(_ceiling);
        var vNegCeil128 = Vector128.Create(-_ceiling);
        int limit128 = count - Vector128<float>.Count;

        for (; i <= limit128; i += Vector128<float>.Count)
        {
            var v = Vector128.LoadUnsafe(ref Unsafe.Add(ref dataRef, i));
            v = Vector128.Min(Vector128.Max(v, vNegCeil128), vCeil128);
            v.StoreUnsafe(ref Unsafe.Add(ref dataRef, i));
        }

        for (; i < count; i++)
        {
            float sample = Unsafe.Add(ref dataRef, i);
            if (sample > _ceiling)
                Unsafe.Add(ref dataRef, i) = _ceiling;
            else if (sample < -_ceiling)
                Unsafe.Add(ref dataRef, i) = -_ceiling;
        }
    }
}
