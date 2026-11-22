using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Domain.Documents;

/// <summary>
/// Dense embedding for a chunk. Stored as a compact float array in a varbinary column and
/// scored in-process, which keeps the deployment single-database and dependency-free.
/// </summary>
public class ChunkEmbedding : Entity
{
    public Guid DocumentChunkId { get; set; }

    public Guid WorkspaceId { get; set; }

    public int Dimensions { get; set; }

    /// <summary>Raw little-endian float32 payload.</summary>
    public byte[] Vector { get; set; } = Array.Empty<byte>();

    public string ModelName { get; set; } = string.Empty;

    public static byte[] Pack(IReadOnlyList<float> values)
    {
        var bytes = new byte[values.Count * sizeof(float)];
        for (var i = 0; i < values.Count; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * sizeof(float), sizeof(float)), values[i]);
        }

        return bytes;
    }

    public static float[] Unpack(byte[] bytes)
    {
        var values = new float[bytes.Length / sizeof(float)];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = BitConverter.ToSingle(bytes, i * sizeof(float));
        }

        return values;
    }
}
