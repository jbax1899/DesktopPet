using System.Buffers.Binary;
using NAudio.Dmo;
using NAudio.Wave;

namespace DesktopPet.App.Audio;

internal static class AudioSampleConverter
{
    public static float[] ToMono(ReadOnlySpan<byte> bytes, WaveFormat format)
    {
        var bytesPerSample = format.BitsPerSample / 8;
        if (bytesPerSample <= 0 || format.Channels <= 0)
        {
            return [];
        }

        var frameSize = bytesPerSample * format.Channels;
        var frameCount = bytes.Length / frameSize;
        var mono = new float[frameCount];
        var isFloat = IsFloat(format);

        for (var frame = 0; frame < frameCount; frame++)
        {
            double sum = 0;
            var frameOffset = frame * frameSize;
            for (var channel = 0; channel < format.Channels; channel++)
            {
                var sampleOffset = frameOffset + (channel * bytesPerSample);
                sum += ReadSample(bytes.Slice(sampleOffset, bytesPerSample), format.BitsPerSample, isFloat);
            }

            mono[frame] = (float)Math.Clamp(sum / format.Channels, -1d, 1d);
        }

        return mono;
    }

    private static bool IsFloat(WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            return true;
        }

        return format is WaveFormatExtensible extensible
            && extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT;
    }

    private static double ReadSample(ReadOnlySpan<byte> bytes, int bitsPerSample, bool isFloat)
    {
        if (isFloat && bitsPerSample == 32)
        {
            return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes));
        }

        return bitsPerSample switch
        {
            8 => (bytes[0] - 128) / 128d,
            16 => BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768d,
            24 => ReadInt24(bytes) / 8388608d,
            32 => BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648d,
            _ => throw new NotSupportedException($"Unsupported audio sample format: {bitsPerSample}-bit {formatName(isFloat)}.")
        };

        static string formatName(bool floatingPoint) => floatingPoint ? "float" : "PCM";
    }

    private static int ReadInt24(ReadOnlySpan<byte> bytes)
    {
        var value = bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
        return (value & 0x800000) != 0 ? value | unchecked((int)0xFF000000) : value;
    }
}
