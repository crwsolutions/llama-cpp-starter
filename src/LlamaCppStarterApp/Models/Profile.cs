namespace LlamaCppStarterApp.Models;

/// <summary>
/// Repo-model voor een opstartprofiel (gekoppeld aan exact één model).
/// Name en Port zijn observable zodat het Startinstellingen-paneel er direct (two-way)
/// aan kan binden; ParamsJson is de JSON-blob van <see cref="ProfileParameters"/>.
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

    /// <summary>Weergavenaam van het basismodel (niet in DB; door de VM gevuld).</summary>
    public string ModelName { get; set; } = string.Empty;
}
