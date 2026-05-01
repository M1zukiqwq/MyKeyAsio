using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using NAudio.Wave;

namespace KeyAsio.Core.Audio.SampleProviders.BalancePans;

public sealed class BalanceSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _sourceProvider;
    private float _leftVolume = 1f;
    private float _rightVolume = 1f;
    private readonly int _channels;

    private static readonly Vector256<int> s_dupLMask256 = Vector256.Create(0, 0, 2, 2, 4, 4, 6, 6);
    private static readonly Vector128<int> s_dupLMask128 = Vector128.Create(0, 0, 2, 2);

    public BalanceSampleProvider(ISampleProvider sourceProvider)
    {
        _sourceProvider = sourceProvider;
        _channels = _sourceProvider.WaveFormat.Channels;
        if (_channels > 2)
            throw new NotSupportedException("channels: " + _channels);
    }

    public float Balance
    {
        get => (_rightVolume - _leftVolume) * 2;
        set
        {
            if (value > 1f) value = 1f;
            else if (value < -1f) value = -1f;

            if (value > 0)
            {
                _leftVolume = 1f - value;
                _rightVolume = 1f + value;
            }
            else if (value < 0)
            {
                _leftVolume = 1f - value;
                _rightVolume = 1f + value;
            }
            else
            {
                _leftVolume = 1f;
                _rightVolume = 1f;
            }
        }
    }

    public WaveFormat WaveFormat => _sourceProvider.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        if (count == 0) return 0;
        int samplesRead = _sourceProvider.Read(buffer, offset, count);
        if (_channels != 2 || Balance == 0) return samplesRead;

        float leftVol = _leftVolume;
        float crossVol = 1f - leftVol;

        int i = 0;
        ref float dataRef = ref buffer[offset];
        int total = samplesRead;

        if (Vector256.IsHardwareAccelerated)
        {
            var vSelf = Vector256.Create(leftVol, 1f, leftVol, 1f, leftVol, 1f, leftVol, 1f);
            var vCross = Vector256.Create(0f, crossVol, 0f, crossVol, 0f, crossVol, 0f, crossVol);
            int limit = total - Vector256<float>.Count;

            for (; i <= limit; i += Vector256<float>.Count)
            {
                var vIn = Vector256.LoadUnsafe(ref Unsafe.Add(ref dataRef, i));
                var vDupL = Vector256.Shuffle(vIn, s_dupLMask256);
                var vOut = vIn * vSelf + vDupL * vCross;
                vOut.StoreUnsafe(ref Unsafe.Add(ref dataRef, i));
            }
        }
        else
        {
            var vSelf = Vector128.Create(leftVol, 1f, leftVol, 1f);
            var vCross = Vector128.Create(0f, crossVol, 0f, crossVol);
            int limit = total - Vector128<float>.Count;

            for (; i <= limit; i += Vector128<float>.Count)
            {
                var vIn = Vector128.LoadUnsafe(ref Unsafe.Add(ref dataRef, i));
                var vDupL = Vector128.Shuffle(vIn, s_dupLMask128);
                var vOut = vIn * vSelf + vDupL * vCross;
                vOut.StoreUnsafe(ref Unsafe.Add(ref dataRef, i));
            }
        }

        for (; i < total; i += 2)
        {
            ref float lRef = ref Unsafe.Add(ref dataRef, i);
            float d0New = lRef * leftVol;
            float d0Diff = lRef - d0New;
            lRef = d0New;
            Unsafe.Add(ref dataRef, i + 1) += d0Diff;
        }

        return samplesRead;
    }
}