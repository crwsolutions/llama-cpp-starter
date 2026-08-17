# llama-cpp-starter

.NET MAUI desktop-app (Windows) die `llama-server.exe` start met per-model opstartprofielen.

## Wat de app doet

- **Runtimes** — kies een map met lokale llama.cpp-builds; de app scant recursief op `llama-server.exe`, detecteert de backend (Cuda/Vulkan/Rocm/Metal/CPU) en onthoudt de runtimes in een lokale SQLite-DB.
- **Modellen** — scant een modelfolder op GGUF-bestanden (naam, kwantificatie, grootte; `*mmproj*.gguf` wordt automatisch gekoppeld). Per model beheer je opstartprofielen: alle `llama-server`-startparameters in één editor-paneel met live command-preview. Profielen worden als JSON-blob opgeslagen; elk nieuw model krijgt een `Default`-profiel (niet te verwijderen).
- **Overzicht** — kies model + startprofiel + runtime en klik **Laden** om de server te starten (live logboek + health-polling op `/health`). **Unload** stopt de server eerst via `POST /exit`, wacht max 30 s en killt anders het procesboom. Bij het afsluiten van de app wordt een draaiende server gestopt.
- **Instellingen** — placeholder (nog te doen). Map-instellingen worden intern al gepersisteerd.

## Architectuur

MVVM (CommunityToolkit.MVVM) met Repository/Services-laag:

```
Models/        : Model, Profile, ProfileParameters, Runtime, LlamaServerState
Repositories/  : IModelRepository, IProfileRepository, IRuntimeRepository, IAppSettingsRepository (Dapper + SQLite)
Services/      : ModelScannerService, RuntimeScannerService, LlamaServerCommandBuilder (pure static),
                 LlamaServerProcessService (singleton), ServerHealthService
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
