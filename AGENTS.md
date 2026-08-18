# AGENTS.md

Richtlijnen voor AI-agents die in dit repo werken.

## Doel

`llama-cpp-starter` is een .NET MAUI desktop-app (Windows) waarmee de gebruiker:

- een **runtime** kiest (lokale `llama-server.exe` build, gevonden via map-scan),
- een **modelfolder** scant op GGUF-bestanden,
- **opstartprofielen per model** beheert (alle `llama-server`-startparameters; `Default` per model is niet te verwijderen),
- een model **laadt/unloadt** via het Overzicht-scherm (server-start, live logboek, health-polling op `/health`).

Scope-grenzen (bewust niet in deze iteratie): live metrics (tokens/s, KV-cache), GPU-probe (nvidia-smi), redeneren/vision-head velden, editabele runtime-command-editor, hardware-kolom, `--fit`-veld, Instellingen-content (placeholder). Zie `.alta/plans/2026-08-17-llama-cpp-starter-core.md` voor het volledige goedgekeurde plan.

## Architectuur

MVVM met Repository/Services-laag. ViewModels roepen services; repos doen puur SQL/Dapper (eigen `SqliteConnection` per call, DB-pad via `Database.DbPath`). Alles is singleton (single-user app); registratie in `MauiProgram.cs` (repos + services) en `Views/AddViewsExtension.cs` (views + viewmodels, VM's als singleton zodat selecties overleven bij navigeren).

```
src/LlamaCppStarterApp/
  Models/        : Model, Profile, ProfileParameters, Runtime, LlamaServerState
                   ProfileParameters = ObservableObject met nullable velden;
                   dubbel gebruik: editor-model (paneel) én JSON-blob (Params-kolom).
                   Null/leeg = vlag niet doorgeven (llama.cpp-default).
  Repositories/  : Database (user_version-migratie 0→1), IModelRepository,
                   IProfileRepository, IRuntimeRepository, IAppSettingsRepository (Dapper)
  Services/      : ModelScannerService (GGUF-scan, quant/mmproj-detectie),
                   RuntimeScannerService (llama-server.exe-scan, backend-heuristiek),
                   LlamaServerCommandBuilder (pure static; volgorde = referentie-opdracht;
                   pad met spaties quoten),
                   LlamaServerProcessService (singleton; één current server; Load/Unload:
                   POST /exit → max 30 s wachten → Kill(entireProcessTree)),
                   ServerHealthService (poll /health elke 2 s terwijl Starting/Running)
  ViewModels/    : BaseViewModel, OverviewViewModel, ModelsViewModel,
                   RuntimesViewModel, SettingsViewModel
  Views/         : AppShell (4 ShellContents, FlyoutBehavior=Locked, glyph-itemtemplate),
                   OverviewPage, ModelsPage, RuntimesPage, SettingsPage
  Converters/    : converters in Converters.xaml (SizeToGb, nullable int/double/bool,
                   PickerDefault, visibility, TitleToGlyph voor flyout-iconen)
```

Kernmechanismen:

- **Database**: `llamacppstarter_data.db` in `FileSystem.AppDataDirectory`. Migraties alleen voorwaarts via `PRAGMA user_version`; nieuwe tabellen/colomms toevoegen = nieuwe versiestap in `Database.Migrate()`.
- **Profielen**: `ProfileParameters` serialiseert naar één JSON-blob in `Profiles.Params` (voorwaarts-compatibel; nieuwe velden kosten geen migratie). Corrupte blob → `ProfileParameters.TryParse` → fallback leeg profiel + melding in UI (móét niet crashen).
- **Command-constructie**: `LlamaServerCommandBuilder.BuildArgs` is pure static en reproduceert de referentie-opdracht uit het plan (vlag-volgorde en double-formattering per veld, bv. `--temp 1.0`, `--min-p 0.00`). Wijzigingen hier handmatig verifiëren (geen test-project).
- **Procesbeheer**: process-events naar UI marshalen via `MainThread.BeginInvokeOnMainThread` (AppendOutput-patroon, log-buffer max ~2000 regels). App-uitgang: `Window.Destroying` → `LlamaServerProcessService.UnloadAsync()` (geen weestprocessen).
- **Map-instellingen**: `ModelsDirectory`/`RuntimeDirectory` in `AppSettings`-tabel; scan-schermen lezen/schrijven ze (niet hard-coden).
- **Navigatie**: Shell-routes `OverviewPage`, `ModelsPage`, `RuntimesPage`, `SettingsPage`; Overzicht → Modellen voor "Add"-profiel via `ModelsViewModel.PendingNewProfileModelId` + `Shell.Current.GoToAsync`.

## Techstack

- **Taal/SDK**: C# (LangVersion preview), .NET 10.
- **UI**: .NET MAUI 10.0.90 (`Microsoft.Maui.Controls`), Windows-desktop (`net10.0-windows10.0.19041.0`, `WindowsPackageType=None`); csproj heeft ook android/ios/maccatalyst TF's — verificatie draait op de Windows-TF.
- **MVVM**: CommunityToolkit.MVVM 8.4.2 (`[ObservableProperty]`/`[RelayCommand]` source gen) + CommunityToolkit.Maui 15.0.0 (`FolderPicker` zit in `CommunityToolkit.Maui.Storage`, API: `FolderPicker.Default.PickAsync()` → `result.IsSuccessful`/`result.Folder.Path`).
- **Data**: Microsoft.Data.Sqlite 10.0.11 + Dapper 2.1.79.
- **Icons**: FontAwesome6 Free Solid (`FontFamily="FontAwesomeSolid"`), geregistreerd in `MauiProgram.cs`; flyout-glyphs via `TitleToGlyphConverter` in `AppShell.xaml`.
- **Stijlen**: `Resources/Styles/Styles.xaml` + app-stijlen (`SectionTitle`, `PanelBorder`, `TableHeaderLabel`, `TableRowBorder`, `RowButton`, `DangerButton`, `FieldLabel`, `FieldEntry`, `FieldPicker`, `LogLabel`). Nederlandse UI-teksten.

## Bouwen & verifiëren

```powershell
dotnet build src/LlamaCppStarterApp -f net10.0-windows10.0.19041.0   # moet 0 warnings / 0 errors zijn
dotnet run --project src/LlamaCppStarterApp -f net10.0-windows10.0.19041.0
```

- Er is bewust **geen test-project** (per gebruiker). `LlamaServerCommandBuilder` is pure static → bij wijziging handmatig verifiëren (bv. tijdelijk console-project dat de bron-bestanden linkt; verwijder het daarna).
- `LineBreakMode` heeft in deze MAUI-versie géén `TailTrunc` (gebruik `CharacterWrap`); `ScrollBarVisibility` heeft géén `Auto` (gebruik `Default`/`Hidden`/`Visible`).
- Gecompileerde bindings (`x:DataType`) staan aan; nieuwe XAML-bindings controleren op MAUIX-fouten bij de build.

## Conventies

- Bestaand patroon volgen: repo per domeinobject met interface, eigen connectie per call; VM's erven van `BaseViewModel` (`IsBusy`, `Title`).
- NL-teksten in de UI; code/comments in bestaande stijl.
- `docs/prototype-schermen.txt` bevat user-wijzigingen: niet committen zonder toestemming.
- Plans staan onder `.alta/plans/` (niet geïgnoreerd) en horen gecommit te worden met de bijbehorende implementatie.
- Geen nieuwe dependencies/frameworks/lagen toevoegen voor kleine wijzigingen; smallest coherent change.
