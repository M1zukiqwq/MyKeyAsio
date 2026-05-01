using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using NAudio.Wave;

namespace KeyAsio.Core.Audio.SampleProviders.Limiters;

/// <summary>
/// A zero-latency soft limiter that leaves quiet signals untouched and gently saturates peaks.
/// SIMD-accelerated for AVX2 and SSE2/AdvSimd targets.
/// </summary>
public sealed class PolynomialLimiterProvider : LimiterBase
{
    private float _threshold;
    private float _ceiling;
    private float _maxOver;

    public PolynomialLimiterProvider(ISampleProvider source, float threshold = 0.8f, float ceiling = 0.99f) :
        base(source)
    {
        UpdateParameters(threshold, ceiling);
    }

    protected override void Process(float[] buffer, int offset, int count)
    {
        float threshold = _threshold;
        float ceiling = _ceiling;
        float maxOver = _maxOver;

        int i = 0;
        ref float dataRef = ref buffer[offset];

        if (Vector256.IsHardwareAccelerated)
        {
            var vThresh = Vector256.Create(threshold);
            var vCeil = Vector256.Create(ceiling);
            var vMaxOver = Vector256.Create(maxOver);
            var vOne = Vector256<float>.One;
            int limit = count - Vector256<float>.Count;

            for (; i <= limit; i += Vector256<float>.Count)
            {
                var vX = Vector256.LoadUnsafe(ref Unsafe.Add(ref dataRef, i));
                var vAbs = Vector256.Abs(vX);
                var processMask = Vector256.GreaterThan(vAbs, vThresh);

                var vOver = vAbs - vThresh;
                var vSoft = vOver / (vOne + vOver / vMaxOver);
                var vResult = Vector256.Min(vThresh + vSoft, vCeil);
                vResult = Vector256.CopySign(vResult, vX);

                vX = Vector256.ConditionalSelect(processMask, vResult, vX);
                vX.StoreUnsafe(ref Unsafe.Add(ref dataRef, i));
            }
        }

        var vThresh128 = Vector128.Create(threshold);
        var vCeil128 = Vector128.Create(ceiling);
        var vMaxOver128 = Vector128.Create(maxOver);
        var vOne128 = Vector128<float>.One;
        int limit128 = count - Vector128<float>.Count;

        for (; i <= limit128; i += Vector128<float>.Count)
        {
            var vX = Vector128.LoadUnsafe(ref Unsafe.Add(ref dataRef, i));
            var vAbs = Vector128.Abs(vX);
            var processMask = Vector128.GreaterThan(vAbs, vThresh128);

            var vOver = vAbs - vThresh128;
            var vSoft = vOver / (vOne128 + vOver / vMaxOver128);
            var vResult = Vector128.Min(vThresh128 + vSoft, vCeil128);
            vResult = Vector128.CopySign(vResult, vX);

            vX = Vector128.ConditionalSelect(processMask, vResult, vX);
            vX.StoreUnsafe(ref Unsafe.Add(ref dataRef, i));
        }

        for (; i < count; i++)
        {
            float x = Unsafe.Add(ref dataRef, i);
            float absX = Math.Abs(x);
            if (absX <= threshold) continue;

            float over = absX - threshold;
            float soft = over / (1.0f + over / maxOver);
            float result = threshold + soft;
            if (result > ceiling) result = ceiling;
            Unsafe.Add(ref dataRef, i) = Math.Sign(x) * result;
        }
    }

    public void UpdateParameters(float threshold, float ceiling)
    {
        _ceiling = Math.Clamp(ceiling, 0.1f, 1.0f);
        _threshold = Math.Clamp(threshold, 0.1f, _ceiling - 0.01f);
        _maxOver = _ceiling - _threshold;
    }

    public static PolynomialLimiterProvider GamePreset(ISampleProvider sampleProvider)
    {
        return new PolynomialLimiterProvider(sampleProvider);
    }
}