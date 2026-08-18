using System.Text;

namespace LlamaCppStarterApp.Services;

/// <summary>
/// Aangebouwde, read-only lezer voor de metadata-kop van een GGUF-bestand.
/// Port uit het referentieproject (LocalLlmConsole): magic "GGUF", versie 1-3,
/// type-tags 0-12, arrays als samenvatting, limieten 1 MiB string / 100.000
/// array-elementen / 64 MiB blok / 512 keys, case-insensitief dictionary.
/// Elke fout (corrupt, te groot, buffer te kort) → leeg dictionary, geen exception.
/// </summary>
public static class GgufMetadataReader
{
    private const uint MinSupportedVersion = 1;
    private const uint MaxSupportedVersion = 3;
    private const ulong MaxMetadataBytes = 64UL * 1024UL * 1024UL;
    private const ulong MaxArrayElements = 300_000UL;

    public static IReadOnlyDictionary<string, object?> TryRead(string path, int maxKeys = 512)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "GGUF") return values;

            var version = reader.ReadUInt32();
            if (version is < MinSupportedVersion or > MaxSupportedVersion) return values;
            _ = reader.ReadUInt64();
            var metadataCount = reader.ReadUInt64();
            var metadataStart = stream.Position;
            var count = (int)Math.Min(metadataCount, (ulong)Math.Max(0, maxKeys));
            for (var i = 0; i < count; i++)
            {
                if ((ulong)(stream.Position - metadataStart) > MaxMetadataBytes) break;
                var key = ReadString(reader);
                var type = (GgufValueType)reader.ReadUInt32();
                values[key] = ReadValue(reader, type);
            }
        }
        catch
        {
            return values;
        }

        return values;
    }

    private static object? ReadValue(BinaryReader reader, GgufValueType type) => type switch
    {
        GgufValueType.UInt8 => reader.ReadByte(),
        GgufValueType.Int8 => reader.ReadSByte(),
        GgufValueType.UInt16 => reader.ReadUInt16(),
        GgufValueType.Int16 => reader.ReadInt16(),
        GgufValueType.UInt32 => reader.ReadUInt32(),
        GgufValueType.Int32 => reader.ReadInt32(),
        GgufValueType.Float32 => reader.ReadSingle(),
        GgufValueType.Bool => reader.ReadByte() != 0,
        GgufValueType.String => ReadString(reader),
        GgufValueType.Array => ReadArraySummary(reader),
        GgufValueType.UInt64 => reader.ReadUInt64(),
        GgufValueType.Int64 => reader.ReadInt64(),
        GgufValueType.Float64 => reader.ReadDouble(),
        _ => null
    };

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadUInt64();
        if (length > 1024 * 1024) throw new InvalidDataException("GGUF string is too large.");
        if (length > (ulong)Math.Max(0, reader.BaseStream.Length - reader.BaseStream.Position))
            throw new EndOfStreamException("GGUF string extends past the end of the file.");
        return Encoding.UTF8.GetString(reader.ReadBytes((int)length));
    }

    private static string ReadArraySummary(BinaryReader reader)
    {
        var elementType = (GgufValueType)reader.ReadUInt32();
        var length = reader.ReadUInt64();
        if (length > MaxArrayElements) throw new InvalidDataException("GGUF metadata array is too large.");
        var fixedSize = FixedSize(elementType);
        if (fixedSize > 0)
        {
            var bytes = checked(length * (ulong)fixedSize);
            if (bytes > MaxMetadataBytes) throw new InvalidDataException("GGUF metadata array is too large.");
            if (bytes > (ulong)Math.Max(0, reader.BaseStream.Length - reader.BaseStream.Position))
                throw new EndOfStreamException("GGUF array extends past the end of the file.");
            reader.BaseStream.Seek(checked((long)bytes), SeekOrigin.Current);
        }
        else if (elementType == GgufValueType.String)
        {
            for (ulong i = 0; i < length; i++)
            {
                var stringLength = reader.ReadUInt64();
                if (stringLength > 1024 * 1024) throw new InvalidDataException("GGUF array string is too large.");
                if (stringLength > (ulong)Math.Max(0, reader.BaseStream.Length - reader.BaseStream.Position))
                    throw new EndOfStreamException("GGUF array string extends past the end of the file.");
                reader.BaseStream.Seek(checked((long)stringLength), SeekOrigin.Current);
            }
        }
        else
        {
            for (ulong i = 0; i < length; i++)
                _ = ReadValue(reader, elementType);
        }
        return $"{length:N0} {elementType} values";
    }

    private static int FixedSize(GgufValueType type) => type switch
    {
        GgufValueType.UInt8 or GgufValueType.Int8 or GgufValueType.Bool => 1,
        GgufValueType.UInt16 or GgufValueType.Int16 => 2,
        GgufValueType.UInt32 or GgufValueType.Int32 or GgufValueType.Float32 => 4,
        GgufValueType.UInt64 or GgufValueType.Int64 or GgufValueType.Float64 => 8,
        _ => 0
    };

    private enum GgufValueType : uint
    {
        UInt8 = 0,
        Int8 = 1,
        UInt16 = 2,
        Int16 = 3,
        UInt32 = 4,
        Int32 = 5,
        Float32 = 6,
        Bool = 7,
        String = 8,
        Array = 9,
        UInt64 = 10,
        Int64 = 11,
        Float64 = 12
    }
}
