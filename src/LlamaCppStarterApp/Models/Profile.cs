namespace LlamaCppStarterApp.Models;

/// <summary>
/// Editor state of the MM-projector picker (ModelsPage, Vision pane).
/// Maps to ProfileParameters.MmprojPath: Auto = null (auto-linked mmproj of the model),
/// Off = empty (no --mmproj), Custom = explicit path (override).
/// </summary>
public enum MmprojMode
{
    Auto,
    Off,
    Custom
}

/// <summary>
/// Repository model for a launch profile (bound to exactly one model).
/// Name and Port are observable so the Startinstellingen panel can bind directly (two-way);
/// ParamsJson is the JSON blob of <see cref="ProfileParameters"/>.
/// </summary>
public partial class Profile : ObservableObject
{
    public int Id { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    public int ModelId { get; set; }

    public bool IsDefault { get; set; }

    [ObservableProperty]
    public partial int Port { get; set; } = 8080;

    public string ParamsJson { get; set; } = string.Empty;

    /// <summary>Display name of the base model (not stored in DB; populated by the VM).</summary>
    public string ModelName { get; set; } = string.Empty;
}
