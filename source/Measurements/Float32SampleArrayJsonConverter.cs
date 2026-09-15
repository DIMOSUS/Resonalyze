using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonalyze;

/// <summary>Base64 float32 LE sample arrays (reads pre-v8 number arrays too). See docs/tech/sweep-measurement.md#impulse-response-file-format.</summary>
internal sealed class Float32SampleArrayJsonConverter : JsonConverter<double[]>
{
    public override double[] Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        // Legacy arrays keep full double precision.
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
                // Beyond float.MaxValue it would round to infinity and only fail on the next load; refuse at save.
                throw new InvalidOperationException(
                    $"Sample {i} ({value[i]}) does not fit a float32.");
            }

            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float)), sample);
        }

        writer.WriteBase64StringValue(bytes);
    }
}
