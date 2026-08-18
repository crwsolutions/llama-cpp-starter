namespace LlamaCppStarterApp.Models;

public class Model
{
    public int Id { get; set; }

    /// <summary>Deterministisch model-id (safe-prefix uit relatief pad + 8-hex SHA256 van het lowercase full path).</summary>
    public string ModelId { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Quant { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? MmprojPath { get; set; }
    public long ScannedAt { get; set; }

    /// <summary>GGUF-metadata-blob (JSON; exact de gescande velden), gevuld bij (re-)scan.</summary>
    public string MetadataJson { get; set; } = string.Empty;

    /// <summary>Capability-cache (JSON: fingerprint + summary + summaryText); null = nog nooit geïnspecteerd.</summary>
    public string? CapabilitiesJson { get; set; }
}
