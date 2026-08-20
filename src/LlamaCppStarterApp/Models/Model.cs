namespace LlamaCppStarterApp.Models;

public class Model
{
    public int Id { get; set; }

    /// <summary>Deterministic model id (safe prefix of the relative path + 8-hex SHA256 of the lowercase full path).</summary>
    public string ModelId { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Quant { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? MmprojPath { get; set; }
    public long ScannedAt { get; set; }

    /// <summary>GGUF metadata blob (JSON; exactly the scanned fields), populated during (re-)scan.</summary>
    public string MetadataJson { get; set; } = string.Empty;

    /// <summary>Capability cache (JSON: fingerprint + summary + summaryText); null = never inspected.</summary>
    public string? CapabilitiesJson { get; set; }
}
