# llama-cpp-starter

.NET MAUI desktop-app (Windows) die `llama-server.exe` start met per-model opstartprofielen.

## Wat de app doet

- **Runtimes** — kies een map met lokale llama.cpp-builds; de app scant recursief op `llama-server.exe`, detecteert de backend (Cuda/Vulkan/Rocm/Metal/CPU) en onthoudt de runtimes in een lokale SQLite-DB.
- **Modellen** — scant een modelfolder op GGUF-bestanden (PascalCase-naam, kwantificatie uit de bestandsnaam of GGUF-metadata, grootte; `*mmproj*.gguf` wordt automatisch gekoppeld; projector/draft/MTP-companionbestanden worden uit de modellijst gehouden). Elk model krijgt GGUF-metadata (JSON-blob in de DB) en bij selectie een capability-samenvatting (architectuur, contextlengte, chat template, vision, MoE, …) — gecachet als JSON-blob bij het model en herlezen zodra het bestand wijzigt. Modellen zijn single-selectbaar; daaronder staan de **Laadprofielen** per model. Per model beheer je opstartprofielen: alle `llama-server`-startparameters in één editor-paneel met live command-preview (incl. auto-resolutie van `--spec-draft-model`, embedded MTP inbegrepen). Profielen worden als JSON-blob opgeslagen; elk nieuw model krijgt een `Default`-profiel met de app-globale defaults (niet te hernoemen/verwijderen).
- **Overzicht** — kies model + startprofiel + runtime en klik **Laden** om de server te starten (live logboek + health-polling op `/health`). **Unload** stopt de server eerst via `POST /exit`, wacht max 30 s en killt anders het procesboom. Bij het afsluiten van de app wordt een draaiende server gestopt. Het midden toont 6 status-kaarten (Modelstatus, Hardware, Stats, Tokens, MTP-tokens, KV-cache): Modelstatus uit lokale status-state, Hardware via **nvidia-smi** (machinewijd, onafhankelijk van een geladen model: per-PID zodra de server draait, anders de volledige GPU-lijst; 10 s-cache; afwezig → "Unavailable"), Stats live uit `/slots` en Tokens/MTP-tokens/KV-cache uit `/metrics` (poll elke 2 s). `/metrics` is standaard uit bij llama-server, daarom heeft elk profiel een **Metrics endpoint**-toggel (default aan) die `--metrics` toevoegt aan de opstartopdracht.
- **Instellingen** — placeholder (nog te doen). Map-instellingen worden intern al gepersisteerd.

## Architectuur

MVVM (CommunityToolkit.MVVM) met Repository/Services-laag:

```
Models/        : Model (ModelId + MetadataJson + CapabilitiesJson), Profile, ProfileParameters, Runtime, LlamaServerState
Repositories/  : IModelRepository, IProfileRepository, IRuntimeRepository, IAppSettingsRepository (Dapper + SQLite, user_version 2)
Services/      : GgufMetadataReader, ModelCompanionService, ModelCapabilityService (pure static),
                 ModelScannerService, RuntimeScannerService, LlamaServerCommandBuilder (pure static),
                 LlamaServerProcessService (singleton, LoadedSession), ServerHealthService,
                 RuntimeMetrics/RuntimeDashboardService (pure static), ModelRuntimeStatusTracker,
                 GpuStatusProbeService/GpuStatusService/GpuSummaryCache (nvidia-smi-alleen),
                 GpuSummaryService, RuntimeMetricSummaryTracker, RuntimeMetricPollerService
ViewModels/    : OverviewViewModel, ModelsViewModel, RuntimesViewModel, SettingsViewModel
Views/         : OverviewPage, ModelsPage, RuntimesPage, SettingsPage (Shell-flyout)
```

Database: `llamacppstarter_data.db` in `FileSystem.AppDataDirectory`, gemigreerd via `PRAGMA user_version`.

## Bouwen / draaien

```powershell
dotnet build src/LlamaCppStarterApp -f net10.0-windows10.0.19041.0
dotnet run --project src/LlamaCppStarterApp -f net10.0-windows10.0.19041.0
```

Standaard mappen (wijzigbaar via "Kiezen" op de Runtimes/Modellen-schermen):

- Modellen: `E:\llama.cpp\models`
- Runtimes: `E:\llama.cpp\llama-local-build`
