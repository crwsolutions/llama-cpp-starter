namespace LlamaCppStarterApp.Models;

public class Runtime
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string? Backend { get; set; }
    public string? Status { get; set; }
    public string? Location { get; set; }
}
