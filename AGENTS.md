# AGENTS.md

Guidance for AI agents working in this repository.

## Purpose

`llama-cpp-starter` is a .NET MAUI desktop app (Windows) that lets the user:

- choose a **runtime** (a local `llama-server.exe` build discovered by scanning a folder),
- scan a **models folder** for GGUF files,
- manage **per-model launch profiles** (all `llama-server` startup parameters; each model's `Default` profile cannot be deleted),
- **load/unload** a model from the Overview screen (start the server, show a live log colored with ANSI via `--log-colors on`, retain up to 2000 lines, and poll `/health`), whose center displays **6 status cards** (Model status, Hardware, Stats, Tokens, MTP tokens, KV cache; data sources: local status state, nvidia-smi, `/slots`, `/metrics`; since 2026-08-23, the KV cache card shows a usage ProgressBar (0..1; 0 = idle/not calculable) below the `{used}/{capacity} t | {allocation}` text on one line).

Scope boundaries (intentionally not included in this iteration): live metrics (tokens/s, KV cache) and GPU probing are in scope on the **Overview screen** (since 2026-08-19; GPU probing uses nvidia-smi only, not AMD/Intel/CPU); they are not in scope on Screens 2/3. Out of scope: reasoning/vision-head fields, an editable runtime-command editor, a hardware column, a `--fit` field, and Settings content (placeholder). See `.alta/plans/2026-08-17-llama-cpp-starter-core.md` for the full approved plan and `.alta/plans/2026-08-19-overzicht-status-kaarten.md` for the status-cards iteration.

## Architecture

MVVM with a Repository/Services layer. ViewModels call services; repositories handle only SQL/Dapper (a separate `SqliteConnection` per call, database path via `Database.DbPath`). Everything is a singleton (single-user app); registration is in `MauiProgram.cs` (repositories + services) and `Views/AddViewsExtension.cs` (views + viewmodels; VMs are singletons so selections persist across navigation).

```
src/LlamaCppStarterApp/
  Models/        : Model (Id int PK + deterministic string ModelId, MetadataJson, CapabilitiesJson),
                   Profile (+ MmprojMode enum), ProfileParameters, Runtime, LlamaServerState
                   ProfileParameters = ObservableObject with nullable fields;
                   used both as the editor model (panel) and as the JSON blob (Params column).
                   Null/empty = do not pass the flag (llama.cpp default).
                   MmprojPath (string?): null = auto-linked model mmproj; empty = disabled (no --mmproj);
                     path = override. Editor = 3-state toggle in ModelsViewModel (MmprojMode).
                   EnableMetrics (bool? = true) = metrics endpoint (--metrics); old blobs without the key → true.
                   GlobalLaunchDefaults = app-wide defaults (reference command) as a static property + JSON,
                   GpuSummary (record: id, name, usage %, temp °C, memory used/available GiB; display text +
                   0..1 bar fractions; TryParseParts = 6 CSV parts → record?; unparseable part → null field),
  Repositories/  : Database (user_version migration 0→2), IModelRepository,
                   IProfileRepository, IRuntimeRepository, IAppSettingsRepository (Dapper)
  Services/      : GgufMetadataReader (pure static; reads GGUF header in binary; any error → empty dictionary),
                   ModelCompanionService (pure static; projector/draft/MTP name markers,
                   FamilyVersion/ParameterSize matching, ResolveDraftModelPath incl. embedded MTP,
                   ModelIdForPath/FriendlyName/InferQuant),
                   ModelCapabilityService (pure static; 12-field summary (original 11 spec fields
                   + HasMtp, MTP/nextn detection → "MTP" chip) + SummaryText chips,
                   Fingerprint + TryReadCached/BuildCacheJson for the DB cache blob),
                   ModelScannerService (GGUF scan with reparse skip, companion exclusion,
                   MetadataJson with 9 fields, Default profile seeding from GlobalLaunchDefaults;
                   ResolveLaunchDefaultsAsync = seed for a new profile (also ModelsViewModel.AddProfile)),
                   RuntimeScannerService (llama-server.exe scan, backend heuristic),
                   LlamaServerCommandBuilder (pure static; order = reference command;
                   + --rope-*, --cache-prompt, --spec-draft-model (draftModelPath argument),
                   --metrics (EnableMetrics is not false; on by default),
                   --log-colors {on,off,auto} (after --metrics; profile parameter, default "on"),
                   -a alias (after --model/--mmproj; profile ModelAlias or app default "{model} [{profile}]";
                   profileName argument; quote paths/aliases containing spaces)),
                   LlamaServerProcessService (singleton; one current server; Load/Unload:
                   server starts with CreateNoWindow (no visible window, but its own
          hidden console); Unload: Ctrl+C (console CTRL_C_EVENT via AttachConsole)
          → wait up to 30 s → Kill(entireProcessTree);
                   ShutdownServer() = synchronous, blocking exit path (app shutdown):
                   Ctrl+C → wait 5 s → Kill(entireProcessTree); suppress StateChanged/LogReceived
                   via _shuttingDown so the UI dispatcher is not accessed;
                   LoadAsync resolves --spec-draft-model via ModelCompanionService;
                   LoadedSession record (Runtime, Model, Parameters, Port, ProcessId) as
                   public Session, null when stopped),
                   ServerHealthService (poll /health every 2 s while Starting/Running),
                   RuntimeMetrics + RuntimeDashboardService (pure static; Prometheus parsing,
                   /slots snapshot, card labels → ported from the reference project),
                   ModelRuntimeStatusTracker (Loading/Loaded/Fallback + Loading Time),
                   GpuStatusProbeService (nvidia-smi only;
                   per-PID UUID match + fallback; both methods → IReadOnlyList<GpuSummary>, any error → empty list),
                   GpuSummaryCache (single-slot key/list/10 s freshness; stores IReadOnlyList<GpuSummary>),
                   GpuSummaryService (nvidia-smi; with a session = per-PID UUID match,
                    otherwise/empty = full --query-gpu list; 10 s cache, "machine" key for the list;
                    Task<IReadOnlyList<GpuSummary>>, empty-list fallback),
                   RuntimeMetricSummaryTracker (rates/totals/last-known per session key;
                   result + display snapshot incl. KvCacheUsagePercent (0..100, null = not calculable)),
                   RuntimeMetricPollerService (poll /slots + /metrics every 2 s; /metrics 501
                   = not enabled → empty list, no error log; MetricsUpdated event;
                   MetricCardsSnapshot = 4 card texts + KvCacheBarValue (0..1, stopped → 0))
  ViewModels/    : BaseViewModel, OverviewViewModel, ModelsViewModel,
                   RuntimesViewModel, SettingsViewModel
  Views/         : AppShell (4 ShellContents, FlyoutBehavior=Locked, glyph item template),
                   OverviewPage, ModelsPage, RuntimesPage, SettingsPage
  Controls/      : CopyIconButton (ContentView; copies Text to clipboard, FA-copy f0c5 →
                   green check f00c ±4 s → copy; used to the right of the "Command" header (Overview)
                   and the "Runtime Command" header (Models); since 2026-10-29)
  Converters/    : converters in Converters.xaml (SizeToGb, nullable int/double/bool,
                   PickerDefault, MmprojMode picker + Custom visibility, visibility, TitleToGlyph for flyout icons)
```

Core mechanisms:

- **Database**: `llamacppstarter_data.db` in `FileSystem.AppDataDirectory`. Migrations are forward-only via `PRAGMA user_version`; adding tables/columns requires a new migration step in `Database.Migrate()`.
- **GGUF metadata**: `GgufMetadataReader.TryRead` reads the GGUF header in binary (read-only, no dependency); any error → empty dictionary. The scan stores a metadata JSON blob (`Model.MetadataJson`, exactly 9 fields: sourceFolder, modelFile, quant, registeredAt, ggufMetadataAvailable, ggufArchitecture, ggufQuantization, ggufContextLength (only when >0), ggufHasChatTemplate).
- **Capabilities**: `ModelCapabilityService` (pure static) inspects on selection (not during scanning) → 12-field summary (incl. `HasMtp`) + `SummaryText` chips. Cache = JSON blob in `Model.CapabilitiesJson` (fingerprint from path/size/last write + projector; `TryReadCached` miss/stale → inspect again, then save via `UpdateCapabilityAsync(ModelId, …)`).
- **Companions**: `ModelCompanionService` (pure static) excludes projector/draft/MTP files during scanning and resolves `--spec-draft-model` (configured path wins; `draft-mtp` + embedded MTP = `nextn_predict_layers > 0` in the main model → no flag). Resolution is used in both `LlamaServerProcessService.LoadAsync` and `ModelsViewModel.UpdateCommandPreview` (same pure static call → preview matches the actual load).
- **GlobalLaunchDefaults**: app-wide defaults (the exact reference command) are stored as the `GlobalLaunchDefaults` row in `AppSettings` (JSON from `ProfileParameters`, migrated/shared with `ProfileParameters.GlobalLaunchDefaults`); the scanner seeds each new Default profile with this value. Models **without MTP** (nextn, detected the same way as the "MTP" chip) get a `Clone()` with `SpecType`/`SpecDraftNMax` set to null (no `--spec-*` flags); the row itself retains the full reference command.
- **Profiles**: `ProfileParameters` serializes to one JSON blob in `Profiles.Params` (forward-compatible; new fields require no migration). Corrupt blob → `ProfileParameters.TryParse` → empty-profile fallback + UI notification (must not crash). The Default profile cannot be renamed (name Entry disabled + `SaveProfileAsync` guard) or deleted.
- **Command construction**: `LlamaServerCommandBuilder.BuildArgs` is pure static and reproduces the reference command from the plan (flag order and per-field double formatting, e.g. `--temp 1.0`, `--min-p 0.00`); added flags: `--rope-*` (after `--cache-type-v`), `--cache-prompt`/`--no-cache-prompt` (after `--ctx-checkpoints`), `--spec-draft-model` (after `--spec-draft-n-max`), `--metrics` (EnableMetrics is not false; on by default; after `--image-min-tokens`),
`--log-colors {on,off,auto}` (after `--metrics`; profile parameter, default `"on"` for new profiles,
via GlobalLaunchDefaults; existing profiles only get the flag if the user sets it),
`-a` (model-name alias(es), comma-separated; after `--model`/`--mmproj`: profile `ModelAlias`, otherwise default `"{model} [{profileName}]"`; always passed), `--log-colors on` (app-specific; always passed; after `--metrics` — ANSI colors in redirected stderr, rendered as `FormattedString` spans in the Overview log). Manually verify changes here (there is no test project).
- **Process management**: marshal process events to the UI via `MainThread.BeginInvokeOnMainThread` (AppendOutput pattern → per-line `LogLines` collection (`ObservableCollection<LogLine>`; ANSI colors from `--log-colors on` → `FormattedString` spans via `AnsiLogFormatter`; `LogLine.Raw` = text without ESC codes for clipboard copying; trim to 2000 lines) for the Overview log `CollectionView`; the newest line stays visible via `ItemsUpdatingScrollMode="KeepLastItemInView"`). On app exit, `Window.Destroying` calls `LlamaServerProcessService.ShutdownServer()` synchronously and blocks the UI thread (Ctrl+C → wait 5 s → `Kill(entireProcessTree)`), ensuring the kill completes before the app exits (no orphan processes). Do NOT use `async void`/`await` in the Destroying handler; suppress StateChanged/LogReceived during shutdown (via `_shuttingDown`), otherwise `MainThread.BeginInvokeOnMainThread` listeners throw "Window was already deactivated" and interrupt the stop sequence before the process is killed.
- **Hardware card**: the nvidia-smi list is machine-wide and independent of a loaded model (since 2026-08-20; no "No loaded model" idle state). `OverviewViewModel` polls `GpuSummaryService` every 10 s (Timer; first probe immediately; with a session = per-PID, otherwise/fallback = full `--query-gpu` list; 10 s cache, "machine" key). The Hardware card shows **one line** per GPU (`GPU {i}: {name} | {u}% | {t}°C | {used}/{total} GiB`) plus **2 horizontal bars** (built-in MAUI `ProgressBar`, 0..1; GPU=StatusActive, memory=Primary) via `DataTemplate x:DataType="models:GpuSummary"` (since 2026-08-21; max 4 rows, hard cap). `OverviewViewModel` = `HardwareGpus` (IReadOnlyList<GpuSummary>) + `HardwareUnavailable` (bool placeholder before the first probe / empty list = "Unavailable"). On app exit, polling stops via `_processService.IsShuttingDown` (no `MainThread.BeginInvokeOnMainThread` after window deactivation).
- **Folder settings**: `ModelsDirectory`/`RuntimeDirectory` in the `AppSettings` table; scan screens read/write them (not hard-coded).
- **Navigation**: Shell routes `OverviewPage`, `ModelsPage`, `RuntimesPage`, `SettingsPage`.

## Tech stack

- **Language/SDK**: C# (LangVersion preview), .NET 10.
- **UI**: .NET MAUI 10.0.90 (`Microsoft.Maui.Controls`), Windows desktop (`net10.0-windows10.0.19041.0`, `WindowsPackageType=None`); the csproj also targets Android/iOS/Mac Catalyst — validation runs on the Windows target framework.
- **MVVM**: CommunityToolkit.MVVM 8.4.2 (`[ObservableProperty]`/`[RelayCommand]` source generation) + CommunityToolkit.Maui 15.0.0 (`FolderPicker` is in `CommunityToolkit.Maui.Storage`; API: `FolderPicker.Default.PickAsync()` → `result.IsSuccessful`/`result.Folder.Path`).
- **Data**: Microsoft.Data.Sqlite 10.0.11 + Dapper 2.1.79.
- **Icons**: FontAwesome6 Free Solid (`FontFamily="FontAwesomeSolid"`), registered in `MauiProgram.cs`; flyout glyphs via `TitleToGlyphConverter` in `AppShell.xaml`.
- **Styles**: `Resources/Styles/Styles.xaml` + app styles (`SectionTitle`, `PanelBorder`, `TableHeaderLabel`, `TableRowBorder`, `RowButton`, `DangerButton`, `FieldLabel`, `FieldEntry`, `FieldPicker`, `LogLabel`). English UI text.

## Building and validation

```powershell
dotnet build src/LlamaCppStarterApp -f net10.0-windows10.0.19041.0   # should have 0 warnings / 0 errors
dotnet run --project src/LlamaCppStarterApp -f net10.0-windows10.0.19041.0
```

- There is intentionally **no test project** (per user). `LlamaServerCommandBuilder` is pure static → manually verify changes (e.g. with a temporary console project linking the source files; remove it afterward).
- In this MAUI version, `LineBreakMode` has no `TailTrunc` (use `CharacterWrap`); `ScrollBarVisibility` has no `Auto` (use `Default`/`Hidden`/`Visible`).
- Compiled bindings (`x:DataType`) are enabled; check new XAML bindings for MAUIX errors during the build.

## Conventions

- Follow existing patterns: one repository per domain object with an interface and its own connection per call; VMs inherit from `BaseViewModel` (`IsBusy`, `Title`).
- UI text (labels, placeholders, hints, status messages, server log lines) and code comments (.cs and .xaml) must be in English. UI references in comments should use English names too.
- Plans live under `.alta/plans/` (not ignored) and should be committed with the corresponding implementation.
- Do not add dependencies/frameworks/layers for small changes; prefer the smallest coherent change.
