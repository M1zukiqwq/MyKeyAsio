using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using NAudio.Wave;

namespace KeyAsio.Core.Audio.SampleProviders.BalancePans;

public sealed class PanSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _sourceProvider;
    private float _balanceValue;
    private readonly int _channels;
    private static readonly Vector256<int> s_swapMask256 = Vector256.Create(1, 0, 3, 2, 5, 4, 7, 6);
    private static readonly Vector128<int> s_swapMask128 = Vector128.Create(1, 0, 3, 2);

    public PanSampleProvider(ISampleProvider sourceProvider)
    {
        _sourceProvider = sourceProvider;
        _channels = _sourceProvider.WaveFormat.Channels;
        if (_channels > 2) throw new NotSupportedException("channels: " + _channels);
    }

    public float Balance
    {
        get => _balanceValue;
        set
        {
            if (value > 1f) value = 1f;
            else if (value < -1f) value = -1f;
            _balanceValue = value;
        }
    }

    public WaveFormat WaveFormat => _sourceProvider.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        if (count == 0) return 0;
        int samplesRead = _sourceProvider.Read(buffer, offset, count);
        if (_channels != 2 || _balanceValue == 0) return samplesRead;

        float g = Math.Abs(_balanceValue);
        float gHalf = g * 0.5f;
        float gLL, gLR, gRL, gRR;

        if (_balanceValue < 0)
        {
            gLL = 1f - gHalf;
            gLR = gHalf;
            gRL = 0f;
            gRR = 1f - g;
        }
        else
        {
            gLL = 1f - g;
            gLR = 0f;
            gRL = gHalf;
            gRR = 1f - gHalf;
        }

        int i = 0;
        ref float dataRef = ref buffer[offset];
        int totalSamples = samplesRead;

        if (Vector256.IsHardwareAccelerated)
        {
            var vGainL = Vector256.Create(gLL, gLR, gLL, gLR, gLL, gLR, gLL, gLR);
            var vGainR = Vector256.Create(gRL, gRR, gRL, gRR, gRL, gRR, gRL, gRR);
            int limit = totalSamples - Vector256<float>.Count;

            for (; i <= limit; i += Vector256<float>.Count)
            {
                var vIn = Vector256.LoadUnsafe(ref Unsafe.Add(ref dataRef, i));
                var vSwapped = Vector256.Shuffle(vIn, s_swapMask256);
                var vOut = vIn * vGainL + vSwapped * vGainR;
                vOut.StoreUnsafe(ref Unsafe.Add(ref dataRef, i));
            }
        }
        else
        {
            var vGainL = Vector128.Create(gLL, gLR, gLL, gLR);
            var vGainR = Vector128.Create(gRL, gRR, gRL, gRR);
            int limit = totalSamples - Vector128<float>.Count;

            for (; i <= limit; i += Vector128<float>.Count)
            {
                var vIn = Vector128.LoadUnsafe(ref Unsafe.Add(ref dataRef, i));
                var vSwapped = Vector128.Shuffle(vIn, s_swapMask128);
                var vOut = vIn * vGainL + vSwapped * vGainR;
                vOut.StoreUnsafe(ref Unsafe.Add(ref dataRef, i));
            }
        }

        for (; i < totalSamples; i += 2)
        {
            float l = Unsafe.Add(ref dataRef, i);
            float r = Unsafe.Add(ref dataRef, i + 1);
            float mono = (l + r) * 0.5f;

            if (_balanceValue < 0)
            {
                Unsafe.Add(ref dataRef, i) = l * gRR + mono * g;     // gRR = 1-g, g = panAmount
                Unsafe.Add(ref dataRef, i + 1) = r * gRR;
            }
            else
            {
                Unsafe.Add(ref dataRef, i) = l * gLL;                // gLL = 1-g
                Unsafe.Add(ref dataRef, i + 1) = r * gRR + mono * g; // gRR = 1-g/2
            }
        }

        return samplesRead;
    }
}