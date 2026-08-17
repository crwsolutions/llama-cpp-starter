namespace LlamaCppStarterApp.Models;

public class Model
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Quant { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? MmprojPath { get; set; }
    public long ScannedAt { get; set; }
}
