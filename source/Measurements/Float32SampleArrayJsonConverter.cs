using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonalyze;

/// <summary>
/// Stores a bulk sample array as the base64 of its little-endian float32
/// values, and reads back either that or the plain JSON number array every
/// file before impulse-response format version 8 carries.
/// </summary>
/// <remarks>
/// The arrays this converter is put on are the megabytes of an impulse-response
/// file: written as indented JSON numbers they cost ~28 bytes per sample, as
/// base64 float32 they cost 5⅓. The precision given up is real but irrelevant —
/// float32 keeps ~7 significant digits (≈ −140 dB relative), far below any
/// measured noise floor — and it is given up only in the FILE: the property
/// stays <c>double[]</c>, so everything downstream of a load computes in double
/// exactly as it always has. Byte order is fixed little-endian by contract,
/// not by host: a stored file outlives the machine that wrote it.
/// </remarks>
internal sealed class Float32SampleArrayJsonConverter : JsonConverter<double[]>
{
    public override double[] Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        // Pre-v8 files carry these arrays as JSON numbers; read them at full
        // double precision — the legacy path must not inherit float32 rounding.
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            List<double> samples = [];
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                samples.Add(reader.GetDouble());
            }

            return samples.ToArray();
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            byte[] bytes = reader.GetBytesFromBase64();
            if (bytes.Length % sizeof(float) != 0)
            {
                throw new JsonException(
                    "The base64 sample block is not a whole number of float32 values.");
            }

            var samples = new double[bytes.Length / sizeof(float)];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = BinaryPrimitives.ReadSingleLittleEndian(
                    bytes.AsSpan(i * sizeof(float)));
            }

            return samples;
        }

        throw new JsonException(
            $"Expected a number array or a base64 sample string, found {reader.TokenType}.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        double[] value,
        JsonSerializerOptions options)
    {
        byte[] bytes = new byte[value.Length * sizeof(float)];
        for (int i = 0; i < value.Length; i++)
        {
            float sample = (float)value[i];
            if (!float.IsFinite(sample))
            {
                // A double past ±float.MaxValue rounds to infinity, which would
                // pass the writer's own finite-double validation and only blow
                // up on the next load. No physical sample is within orders of
                // magnitude of this; refuse at the save rather than store it.
                throw new InvalidOperationException(
                    $"Sample {i} ({value[i]}) does not fit a float32.");
            }

            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float)), sample);
        }

        writer.WriteBase64StringValue(bytes);
    }
}
