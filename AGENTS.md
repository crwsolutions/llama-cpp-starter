# AGENTS.md

Richtlijnen voor AI-agents die in dit repo werken.

## Doel

`llama-cpp-starter` is een .NET MAUI desktop-app (Windows) waarmee de gebruiker:

- een **runtime** kiest (lokale `llama-server.exe` build, gevonden via map-scan),
- een **modelfolder** scant op GGUF-bestanden,
- **opstartprofielen per model** beheert (alle `llama-server`-startparameters; `Default` per model is niet te verwijderen),
- een model **laadt/unloadt** via het Overzicht-scherm (server-start, live logboek, health-polling op `/health`), waar het midden **6 status-kaarten** toont (Modelstatus, Hardware, Stats, Tokens, MTP-tokens, KV-cache; data-bronnen: lokaal status-state, nvidia-smi, `/slots`, `/metrics`).

Scope-grenzen (bewust niet in deze iteratie): live metrics (tokens/s, KV-cache) en GPU-probe zijn in scope op het **Overzicht-scherm** (sinds 2026-08-19; GPU-probe = nvidia-smi alleen, geen AMD/Intel/CPU); op Scherm 2/3 niet. Buiten scope: redeneren/vision-head velden, editabele runtime-command-editor, hardware-kolom, `--fit`-veld, Instellingen-content (placeholder). Zie `.alta/plans/2026-08-17-llama-cpp-starter-core.md` voor het volledige goedgekeurde plan en `.alta/plans/2026-08-19-overzicht-status-kaarten.md` voor de status-kaarten-iteratie.

## Architectuur

MVVM met Repository/Services-laag. ViewModels roepen services; repos doen puur SQL/Dapper (eigen `SqliteConnection` per call, DB-pad via `Database.DbPath`). Alles is singleton (single-user app); registratie in `MauiProgram.cs` (repos + services) en `Views/AddViewsExtension.cs` (views + viewmodels, VM's als singleton zodat selecties overleven bij navigeren).

```
src/LlamaCppStarterApp/
  Models/        : Model (Id int PK + ModelId deterministisch string, MetadataJson, CapabilitiesJson),
                   Profile (+ MmprojMode-enum), ProfileParameters, Runtime, LlamaServerState
                   ProfileParameters = ObservableObject met nullable velden;
                   dubbel gebruik: editor-model (paneel) én JSON-blob (Params-kolom).
                   Null/leeg = vlag niet doorgeven (llama.cpp-default).
                   MmprojPath (string?): null = auto-gekoppelde mmproj van het model; leeg = uit (geen --mmproj);
                     pad = override. Editor = 3-standen toggle in ModelsViewModel (MmprojMode).
                   EnableMetrics (bool? = true) = metrics endpoint (--metrics); oude blobs zonder key → true.
                   GlobalLaunchDefaults = app-globale defaults (referentie-opdracht) als statische property + JSON,
                   GpuSummary (record: id, naam, usage %, temp °C, mem used/available GiB; display-texts +
                   0..1-bar-fractions; TryParseParts = 6 CSV-delen → record?; on-parsebaar → null veld),
  Repositories/  : Database (user_version-migratie 0→2), IModelRepository,
                   IProfileRepository, IRuntimeRepository, IAppSettingsRepository (Dapper)
  Services/      : GgufMetadataReader (pure static; binair GGUF-kop lezen; elke fout → leeg dict),
                   ModelCompanionService (pure static; projector/draft/MTP-naammarkers,
                   FamilyVersion/ParameterSize-match, ResolveDraftModelPath incl. embedded MTP,
                   ModelIdForPath/FriendlyName/InferQuant),
                   ModelCapabilityService (pure static; 12-veldensamenvatting (originiele 11 spec-velden
                   + HasMtp, MTP/nextn-detectie → "MTP"-chip) + SummaryText-chips,
                   Fingerprint + TryReadCached/BuildCacheJson voor de DB-cache-blob),
                   ModelScannerService (GGUF-scan reparse-skip, companion-uitsluiting,
                   MetadataJson met 9 velden, Default-profiel-seeding vanuit GlobalLaunchDefaults;
                   ResolveLaunchDefaultsAsync = seed voor nieuw profiel (ook ModelsViewModel.AddProfile)),
                   RuntimeScannerService (llama-server.exe-scan, backend-heuristiek),
                   LlamaServerCommandBuilder (pure static; volgorde = referentie-opdracht;
                   + --rope-*, --cache-prompt, --spec-draft-model (draftModelPath-argument),
                   --metrics (EnableMetrics is not false; default aan); pad met spaties quoten),
                   LlamaServerProcessService (singleton; één current server; Load/Unload:
                   POST /exit → max 30 s wachten → Kill(entireProcessTree);
                   ShutdownServer() = synchroon, blokkerend exit-pad (app-uitgang):
                   POST /exit 2 s → 5 s wachten → Kill(entireProcessTree); StateChanged/LogReceived
                   onderdrukt via _shuttingDown zodat de UI-dispatcher niet geraakt wordt;
                   LoadAsync lost --spec-draft-model op via ModelCompanionService;
                   LoadedSession record (Runtime, Model, Parameters, Port, ProcessId) als
                   public Session, null bij stop),
                   ServerHealthService (poll /health elke 2 s terwijl Starting/Running),
                   RuntimeMetrics + RuntimeDashboardService (pure static; Prometheus-parsing,
                   /slots-snapshot, kaart-labels → port uit het referentieproject),
                   ModelRuntimeStatusTracker (Loading/Loaded/Fallback + Loading Time),
                   GpuStatusProbeService (nvidia-smi-alleen;
                   per-PID uuid-match + fallback; beide methods → IReadOnlyList<GpuSummary>, elke fout → lege lijst),
                   GpuSummaryCache (single-slot key/lijst/10 s-freshness; bewaart IReadOnlyList<GpuSummary>),
                   GpuSummaryService (nvidia-smi; met sessie = per-PID uuid-match,
                    anders/leeg = volledige --query-gpu-lijst; 10 s-cache, "machine"-key voor de lijst;
                    Task<IReadOnlyList<GpuSummary>>, empty-list fallback),
                   RuntimeMetricSummaryTracker (rates/totals/last-known per sessie-key),
                   RuntimeMetricPollerService (poll /slots + /metrics elke 2 s; /metrics 501
                   = niet ingeschakeld → leeg-lijst, géén fout-log; event MetricsUpdated)
  ViewModels/    : BaseViewModel, OverviewViewModel, ModelsViewModel,
                   RuntimesViewModel, SettingsViewModel
  Views/         : AppShell (4 ShellContents, FlyoutBehavior=Locked, glyph-itemtemplate),
                   OverviewPage, ModelsPage, RuntimesPage, SettingsPage
  Converters/    : converters in Converters.xaml (SizeToGb, nullable int/double/bool,
                   PickerDefault, MmprojMode-picker + Custom-visibility, visibility, TitleToGlyph voor flyout-iconen)
```

Kernmechanismen:

- **Database**: `llamacppstarter_data.db` in `FileSystem.AppDataDirectory`. Migraties alleen voorwaarts via `PRAGMA user_version`; nieuwe tabellen/colomms toevoegen = nieuwe versiestap in `Database.Migrate()`.
- **GGUF-metadata**: `GgufMetadataReader.TryRead` leest de GGUF-kop binair (alleen-lezen, geen dependency); elke fout → leeg dictionary. De scan slaat een metadata-JSON op (`Model.MetadataJson`, exact 9 velden: sourceFolder, modelFile, quant, registeredAt, ggufMetadataAvailable, ggufArchitecture, ggufQuantization, ggufContextLength (>0 alleen), ggufHasChatTemplate).
- **Capabilities**: `ModelCapabilityService` (pure static) inspecteert per selectie (niet bij scan) → 12-veldensamenvatting (incl. `HasMtp`) + `SummaryText`-chips. Cache = JSON-blob in `Model.CapabilitiesJson` (fingerprint op path/size/lastwrite + projector; `TryReadCached` → miss/stale = her-inspecteren, daarna `UpdateCapabilityAsync(ModelId, …)` opslaan).
- **Companions**: `ModelCompanionService` (pure static) uitsluit projector/draft/MTP-bestanden bij de scan en lost `--spec-draft-model` op (configured-pad wint; `draft-mtp` + embedded MTP = `nextn_predict_layers > 0` in hoofdmiddel → geen flag). Resolutie zit in `LlamaServerProcessService.LoadAsync` én `ModelsViewModel.UpdateCommandPreview` (zelfde pure static call → preview = echte load).
- **GlobalLaunchDefaults**: app-globale defaults (exacte referentie-opdracht) als `AppSettings`-rij `GlobalLaunchDefaults` (JSON van `ProfileParameters`, gemigreerd/gedeeld met `ProfileParameters.GlobalLaunchDefaults`); de scanner seedt elk nieuw Default-profiel met deze waarde — modellen **zonder MTP** (nextn, zelfde detectie als de "MTP"-chip) krijgen een `Clone()` met `SpecType`/`SpecDraftNMax` null (geen `--spec-*`-vlaggen); de rij zelf blijft de volledige referentie-opdracht.
- **Profielen**: `ProfileParameters` serialiseert naar één JSON-blob in `Profiles.Params` (voorwaarts-compatibel; nieuwe velden kosten geen migratie). Corrupte blob → `ProfileParameters.TryParse` → fallback leeg profiel + melding in UI (móét niet crashen). Default-profiel is niet te hernoemen (naam-Entry disabled + `SaveProfileAsync`-guard) en niet te verwijderen.
- **Command-constructie**: `LlamaServerCommandBuilder.BuildArgs` is pure static en reproduceert de referentie-opdracht uit het plan (vlag-volgorde en double-formattering per veld, bv. `--temp 1.0`, `--min-p 0.00`); nieuwe vlaggen: `--rope-*` (na `--cache-type-v`), `--cache-prompt`/`--no-cache-prompt` (na `--ctx-checkpoints`), `--spec-draft-model` (na `--spec-draft-n-max`), `--metrics` (EnableMetrics is not false; default aan; na `--image-min-tokens`). Wijzigingen hier handmatig verifiëren (geen test-project).
- **Procesbeheer**: process-events naar UI marshalen via `MainThread.BeginInvokeOnMainThread` (AppendOutput-patroon → per-regel `LogLines`-collectie (`ObservableCollection<string>`, trim max 2000 regels) voor het Overzicht-logboek-`CollectionView`; smart auto-follow (volg onderkant tenzij gebruiker omhoog scrolt) in de `OverviewPage` code-behind via `Scrolled` + `CollectionChanged`). App-uitgang: `Window.Destroying` → `LlamaServerProcessService.ShutdownServer()` = synchroon, blokkerend op de UI-thread (POST /exit 2 s → 5 s wachten → `Kill(entireProcessTree)`), zodat het kill doorloopt vóór de app stopt (geen weestprocessen). GEEN `async void`/`await` in de Destroying-handler; tijdens shutdown StateChanged/LogReceived onderdrukken (via `_shuttingDown`), anders gooien de `MainThread.BeginInvokeOnMainThread`-listeners "Window was already deactivated" en breekt de stop-sequence af vóór het kill.
- **Hardware-kaart**: nvidia-smi-lijst is machinewijd en onafhankelijk van een geladen model (sinds 2026-08-20; géén "No loaded model"-idle-state). De `OverviewViewModel` pollt `GpuSummaryService` elke 10 s (Timer; eerste probe direct; met sessie = per-PID, anders/val = volledige `--query-gpu`-lijst; 10 s-cache, "machine"-key). Hardware-kaart toont per GPU **één regel** (`GPU {i}: {naam} | {u}% | {t}°C | {used}/{total} GiB`) + **2 horizontale bars** (ingebouwde MAUI `ProgressBar`, 0..1; GPU=StatusActive, mem=Primary) via `DataTemplate x:DataType="models:GpuSummary"` (sinds 2026-08-21; max 4 rijen, harde cap). `OverviewViewModel` = `HardwareGpus` (IReadOnlyList<GpuSummary>) + `HardwareUnavailable` (bool placeholder vóór eerste probe / lege lijst = "Unavailable"). App-uitgang: poll stopt via `_processService.IsShuttingDown` (géén `MainThread.BeginInvokeOnMainThread` na window-deactivering).
- **Map-instellingen**: `ModelsDirectory`/`RuntimeDirectory` in `AppSettings`-tabel; scan-schermen lezen/schrijven ze (niet hard-coden).
- **Navigatie**: Shell-routes `OverviewPage`, `ModelsPage`, `RuntimesPage`, `SettingsPage`.

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
- NL-teksten in de UI (labels, placeholders, hints, statusmeldingen, serverlog-regels); code-comments (.cs én .xaml) in het Engels (sinds 2026-08-20; UI-referenties in comments, bv. "Startinstellingen"-paneel of "Modelstatus"-kaart, mogen NL blijven).
- Plans staan onder `.alta/plans/` (niet geïgnoreerd) en horen gecommit te worden met de bijbehorende implementatie.
- Geen nieuwe dependencies/frameworks/lagen toevoegen voor kleine wijzigingen; smallest coherent change.
