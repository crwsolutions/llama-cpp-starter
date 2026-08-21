# Plan: llama-cpp-starter core (runtimes, modellen/profielen, overview laad/onlaad)

- Status: Approved
- Plan file: `.alta/plans/2026-08-17-llama-cpp-starter-core.md`
- Created: 2026-08-17
- Task: Bouw een .NET MAUI app die llama-server.exe start met per-model opstartprofielen, met 4 schermen (Overzicht, Modellen, Runtimes, Instellingen) en Repository/Services-architectuur.
- Git: `.alta/plans/` is niet geïgnoreerd; commit dit planfile samen met de bijbehorende implementatie. `docs/prototype-schermen.txt` heeft ongecommitte user-wijzigingen → NIET aanraken/committen zonder toestemming.

## Objective
- Gebruiker kiest een runtime (lokale llama.cpp build), scant een modelfolder op GGUF-bestanden, beheert opstartprofielen per model (Default kan niet), en laad/unloadt een model via het Overzicht-scherm.
- Alleen de parameters uit de door de gebruiker opgegeven opdrachtregel (zie onder). Alle overige velden uit het prototype (live metrics, GPU-probe, redeneren, vision-head, runtime command editor, hardware-kolom) blijven voor nu AWEG.
- Werkvolgorde van achteren naar voren: 1) Runtimes, 2) Modellen + profielen, 3) Overzicht, 4) Instellingen (placeholder).

## Context and evidence
- Bestaand skelet: `src/LlamaCppStarterApp` (.NET MAUI, `WindowsPackageType=None`, draait desktop op `net10.0-windows10.0.19041.0`; csproj heeft nog meervoudige TF's — die blijven, verificatie draait op de Windows-TF).
- MVVM: CommunityToolkit.MVVM 8.4.2 + CommunityToolkit.Maui 15.0.0; views/viewmodels geregistreerd in `Views/AddViewsExtension.cs`; repos geregistreerd in `MauiProgram.cs`; `App.Services` staat beschikbaar voor een-lifecycle hook.
- Navigatie: `AppShell.xaml` heeft al `Shell.FlyoutBehavior="Locked"` + FlyoutWidth=260 (hamburger-sidebar) met nu 1 ShellContent (MainPage).
- Database: `Repositories/Database.cs` maakt `llamacppstarter_data.db` aan in `FileSystem.AppDataDirectory` en heeft een lege `Migrate()` (nu een goed moment om een versiegeschiedenis in te voeren).
- Repo-patroon: `IPromptRepository`/`PromptRepository` (Dapper + Microsoft.Data.Sqlite).
- Te verwijderen testproces: `MainViewModel.RunHelloAsync` (`E:\temp\hello.exe`) + "Run hello.exe" UI in `Views/MainPage.xaml`.
- Parameters (referentie-opdracht, bron: gebruikersverzoek):
  `-m <model> -mm <mmproj> --host 0.0.0.0 --port 8080 --ctx-size 192144 --split-mode layer -ngl 999 --batch-size 256 --ubatch-size 256 --threads 8 --temp 1.0 --top-p 0.95 --top-k 20 --min-p 0.00 --flash-attn on --tensor-split 24,8 --no-host --cache-type-k q8_0 --cache-type-v q8_0 -np 1 --presence-penalty 0.0 --repeat-penalty 1.0 --jinja --keep 1024 --ctx-checkpoints 128 --spec-type draft-mtp --spec-draft-n-max 4 --image-min-tokens 1024`
  Alle vlaggen komen voor in `docs/llama-server-help.txt` (lijnr.: -m 171, -mm 449, --host 476, --port 479, -c 25, -sm 130, -ngl 127, -b 29, -ub 31, -t 7, --temp 250, --top-p 253, --top-k 251, --min-p 254, -fa 39, -ts 138, --no-host 73, -ctk/-ctv 75/79, -np 443, --presence-penalty 262, --repeat-penalty 261, --jinja 590, --keep 33, --ctx-checkpoints 414, --spec-type 369, --spec-draft-n-max 345, --image-min-tokens 461).
- Prototypen: `docs/prototype-schermen.txt` (4 schermen) + screenshots `art/overview.png`, `art/models.png`, `art/runtimes.png`.
- Er is nog geen test-project in `llama-cpp-starter.slnx` (per gebruiker ook niet gewenst).

## Assumptions and open decisions
Alle open beslissingen zijn met de gebruiker opgelost (2026-08-17):
- Windows is de enige doelomgeving voor deze iteratie. Code blijft cross-platform compilabel; verificatie op de Windows-TF.
- Unload: eerst best-effort `POST http://{host}:{port}/exit` (5 s HTTP-timeout, errors negeren), daarna max **30 s** wachten op proceseindiging, daarna `Kill(entireProcessTree: true)`.
- Profiel is gekoppeld aan exact één model.
- Instellingen-scherm = **placeholder** ("nog te doen"); geen content.
- Geen test-project. `LlamaServerCommandBuilder` is een pure statische class → handmatig verifiëren.
- "GPU mode" op de schermen = `--split-mode {none,layer,row,tensor}` (default `layer`). Geen `--fit`-veld.
- Opslag: alle startparameters van een profiel gaan in één strongly-typed class `ProfileParameters` (een `ObservableObject`, die óók als editor-model voor het rechtsonder-paneel dient) en worden als **JSON-blob** in één `Params TEXT`-kolom opgeslagen (geen per-parameter kolommen). Voorwaarts-compatibel: nieuwe velden kosten geen DB-migratie.
- mmproj: bij modelscan wordt automatisch een `*mmproj*.gguf` in dezelfde map gekoppeld; `ProfileParameters.MmprojPath` is override (null = gekoppelde auto; expliciet leeg = uit).

## Design notes

### Architectuur (repository + services, bestaand patroon)
```
Models/            : Model, Profile, ProfileParameters, Runtime, AppSettings (POCO's/enum-waarden)
Repositories/      : IPromptRepository (bestaand, blijft), IModelRepository, IProfileRepository,
                     IRuntimeRepository, IAppSettingsRepository   (Dapper, eigen connection per call)
Services/          : ModelScannerService, RuntimeScannerService, LlamaServerCommandBuilder (pure static),
                     LlamaServerProcessService (singleton), ServerHealthService
ViewModels/ + Views: OverviewViewModel/OverviewPage (vervangt Main*), ModelsViewModel/ModelsPage,
                     RuntimesViewModel/RuntimesPage, SettingsViewModel/SettingsPage
```
- Repositories: puur SQL/Dapper. Services: bestandsysteem, procesbeheer, health-polling. ViewModels: binding + command's, roepen services.
- `MauiProgram.cs`: registreer alle repos + services (alles singleton; past bij deze single-user app).

### Database-schema (extenderen in `Database.cs`)
- Migratie: `PRAGMA user_version` (0→1); bij `user_version == 0`: `CREATE TABLE IF NOT EXISTS` voor de nieuwe tabellen. Bestaande DB's (alleen `PromptEntries`) migreren zo schoon door; `PromptEntries` blijft onaangetast.
- Tabellen:
  - `Models (Id INTEGER PK, Path TEXT UNIQUE NOT NULL, Name TEXT, Quant TEXT, SizeBytes INTEGER, MmprojPath TEXT NULL, ScannedAt INTEGER)`
  - `Profiles (Id INTEGER PK, Name TEXT NOT NULL, ModelId INTEGER NOT NULL REFERENCES Models(Id) ON DELETE CASCADE, IsDefault INTEGER NOT NULL DEFAULT 0, Port INTEGER NOT NULL DEFAULT 8080, Params TEXT NOT NULL)`
    - `Params` = JSON-serialisatie van `ProfileParameters` (System.Text.Json). Unieke index op `(Name, ModelId)`.
  - `Runtimes (Id INTEGER PK, Name TEXT NOT NULL, ExecutablePath TEXT NOT NULL, Backend TEXT, Status TEXT, Location TEXT, CreatedAt INTEGER)`
  - `AppSettings (Key TEXT PK, Value TEXT)`
- Seeds (na initialize/migrate, idempotent): `ModelsDirectory` (default `E:\llama.cpp\models`), `RuntimeDirectory` (default `E:\llama.cpp\llama-local-build`).

### Domain models
- `Model { int Id; string Path; string Name; string Quant; long SizeBytes; string? MmprojPath; }`
- `Profile { int Id; string Name; int ModelId; bool IsDefault; int Port; string ParamsJson; }` (repo-lager; VM (de)serialiseert naar `ProfileParameters` via statische `ProfileParameters.FromJson/ToJson`).
- `ProfileParameters : ObservableObject` — één class met alle startparameters (null = vlag niet doorgeven, llama.cpp-default):
  - Basis: `CtxSize (int?)`, `SplitMode (string?, UI-label "GPU mode", {none,layer,row,tensor}, default "layer")`, `Ngl (string?, bv. "999"/"auto"/"all")`, `TensorSplit (string?, bv. "24,8")`, `Threads (int?)`, `HostBind (string?, default "0.0.0.0" = --host-waarde)`, `NoHost (bool?)`, `Parallel (int?)`, `Keep (int?)`, `CtxCheckpoints (int?)`
  - Prestaties: `BatchSize (int?)`, `UbatchSize (int?)`, `FlashAttn (string? {auto,on,off})`, `CacheTypeK/V (string? {f32,f16,bf16,q8_0,q4_0,q4_1,iq4_nl,q5_0,q5_1})`
  - Speculatie: `SpecType (string? {none,draft-simple,draft-eagle3,draft-mtp,draft-dflash,draft-dspark,ngram-simple,ngram-map-k,ngram-map-k4v,ngram-mod,ngram-cache})`, `SpecDraftNMax (int?)`
  - Vision: `MmprojPath (string?)`, `ImageMinTokens (int?)`
  - Generatie: `Temperature/TopP/MinP (double?)`, `TopK (int?)`, `PresencePenalty/RepeatPenalty (double?)`, `Jinja (bool? default true)`
  - `[ObservableProperty]` per veld → rechtsonder-paneel bindt direct aan deze instantie; live command-preview volgt daarop.
- `Runtime { int Id; string Name; string ExecutablePath; string? Backend; string? Status; string? Location; }`
- `LlamaServerState` enum: `Idle | Starting | Running | Stopping`.

### Services
- `ModelScannerService.ScanAsync(string dir)`: recursief `*.gguf` (geen `*mmproj*.gguf` in modellijst — die worden gekoppeld), `FileInfo.Length` voor grootte; quant detecteren via regex op bestandsnaam (`Q4_K_M`, `Q5_K_XL`, `BF16`, `F16`, `IQ4_XS`, …; anders "unknown"); mmproj koppelen: eerste `*mmproj*`-gguf in dezelfde folder. Upsert op `Path`; verdwenen bestanden verwijderen bij her-scan van dezelfde map (plus "Delete"-knop).
- `RuntimeScannerService.ScanAsync(string dir)`: recursief `llama-server.exe` zoeken; `Name` = buildmap-naam, `Backend` = heuristiek op map-/bestandsnaam (`cuda`, `vulkan`, `rocm`, `hip`, `metal`, anders "CPU"), `Status` = "Built Native", `Location` = map. Upsert op `ExecutablePath`; Delete per rij.
- `LlamaServerCommandBuilder` (pure static):
  - `string[] BuildArgs(Runtime, Model, ProfileParameters)` — volgorde = referentie-opdracht: `--model {model.Path}`, `--mmproj {mmproj}` (alleen indien profiel-override of model-koppeling), `--host {bind of 0.0.0.0}`, `--port {port}`, `--ctx-size`, `--split-mode` (default `layer`), `--gpu-layers`, `--tensor-split`, `--batch-size`, `--ubatch-size`, `--threads`, `--flash-attn`, `--cache-type-k`, `--cache-type-v`, `--no-host`, `--parallel`, `--keep`, `--ctx-checkpoints`, `--spec-type`, `--spec-draft-n-max`, `--image-min-tokens`, `--temp`, `--top-p`, `--top-k`, `--min-p`, `--presence-penalty`, `--repeat-penalty`, `--jinja` (true = `--jinja`, false = `--no-jinja`, null = niet meegeven).
  - Null/leeg = vlag weglaten (llama.cpp default). Padden met spaties escapen (`"C:\path with space\file.gguf"`).
  - `string BuildCommandLine(string[] args)` voor de read-only preview in het profielpaneel.
- `LlamaServerProcessService` (singleton, één "current server"):
  - `Task<bool> LoadAsync(Runtime, Model, ProfileParameters, int port)`: valideer (bestanden bestaan, niet al bezig), `ProcessStartInfo { FileName = runtime.ExecutablePath, UseShellExecute=false, RedirectStandardOutput/Error=true, CreateNoWindow=true, WorkingDirectory = runtime.Location }`; `OutputDataReceived`/`ErrorDataReceived` → `event EventHandler<ServerLogEventArgs>` (stderr vooraan `[stderr]`, bestaand `AppendOutput`-patroon), `event EventHandler<ServerStateChangedEventArgs>`.
  - `Task UnloadAsync()`: als Running → best-effort `POST {host}/{port}/exit` (5 s, errors negeren) → `WaitForExitAsync` **30 s** timeout → anders `Kill(entireProcessTree: true)`.
  - Status: `State`, `Port`, `ModelName`, `LastExitCode`; `CheckAlive()` via `process.HasExited`.
  - App-uitgang: `App.OnExit` → `await processService.UnloadAsync()` zodat er geen weestproces achterblijft.
- `ServerHealthService`: licht poll op `http://{host}:{port}/health` (elke 2 s, alleen terwijl Running) → `bool Healthy` + `event`; host = `HostBind` indien gelokaliseerd, anders `127.0.0.1`.

### Schermen
1. **Overzicht (OverviewPage, vervangt MainPage)** — topbar: Model-dropdown, Startprofiel-dropdown (profielen van geselecteerd model), Runtime-dropdown, label met modelmap-pad, knoppen "Laden" (primair) + "Unload" (enabled alleen indien draaiend). Midden: sectie "Modelbestanden" (tabel: Naam, Kwantificatie, Grootte, Open Folder, Delete — geen Hardware-kolom) + "Live runtime-logboek" (log-tekstveld, ViewModel-buffer max ~2000 regels). Onder: "Opgeslagen modelvarianten" (tabel: Naam, Basismodel, Poort, Verwijderen; "Add" navigeert naar Modellen-scherm in profiel-editor). Statusregels: "No runtime is loaded for the selected model." / "Loading… (port 8080)" / "Running (port 8080)".
2. **Modellen (ModelsPage)** — topbar: "Modellenmap scannen" (primair) + "Kiezen" (FolderPicker), pad-label. Midden: modeltabel (zelfde kolommen als Overzicht). Onder: profieltabel (Naam, Basismodel, Poort, Verwijderen; "Add" maakt een leeg profiel van het geselecteerde model; "Default" krijgt geen Verwijderen-knop). Rechtsonder-paneel "Startinstellingen" (editabel, direct gebonden aan de geselecteerde `ProfileParameters`-instantie): Naam, Poort, alle secties (Basisstart, Prestaties & Geheugen, Speculatie/MTP, Vision, Standaardwaarden generatie) + read-only "Runtime Command"-preview (live bij invoer). Knoppen: "Opslaan" (serialiseert `ProfileParameters` → JSON-blob → `IProfileRepository.UpsertAsync`) + "Verwijderen" (Default geblokkeerd). Nieuw model zonder profiel → seed `Default` (port 8080, `Params` = lege JSON).
   - Dropdown-opties komen uit de enums/const-lijsten bij `ProfileParameters` (zie domain models).
3. **Runtimes (RuntimesPage)** — topbar: "Map kiezen" (FolderPicker; na kiezen direct scannen) + pad-label. Tabel: Naam, Backend, Status, Locatie, Actie (Delete = alleen DB-rij, geen bestanden verwijderen!).
4. **Instellingen (SettingsPage)** — placeholder: titel + "Instellingen — nog te doen". (Map-instellingen worden intern toch gepersisteerd in `AppSettings`, zodat scan-schermen ze bij herstart hergebruiken.)

### UI-conventies
- Nederlandse UI-teksten (bestaand prototype).
- Iconen: FontAwesome6FreeSolid (al geregistreerd) voor flyout-items (huis, map, code, tandenwiel).
- Bestaande stijlen uit `Resources/Styles/Styles.xaml`; sectie-look (verticale balk + titel) als Border/Grid opbouwen.
- `Converters/Converters.xaml`: `SizeToGbConverter` (long → "18,7 GB", NL-culture) toevoegen.

## Risks and challenges
- Groot model (27B): laden duurt lang; logboek moet streamen tijdens "Loading" en UI mag niet vriesen (reeds async patroon uit `MainViewModel` hergebruiken).
- Weestprocessen: app mag `llama-server.exe` niet achterlaten bij crash → `OnExit`-hook + `Kill(entireProcessTree)` fallback.
- Poort-conflict: server crasht direct bij bezet poort; fout zichtbaar in logboek (acceptabel; pre-check = out of scope).
- Pad met spaties: correct escapen in command line (manual test met pad met spatie).
- Bestaande DB zonder nieuwe tabellen: migratie via `user_version`.
- JSON-blob: bij corrupte/oude blob → fallback naar lege `ProfileParameters` + foutmelding in UI (niet crashen).
- Geen test-project: risico op regressie in command builder → pure static class + handmatige checklist (zie verification).
- Werkboom: `docs/prototype-schermen.txt` heeft ongecommitte wijzigingen → NIET aanraken/committen.

## Implementation checklist
### Fase 0 — fundament
- [x] `Models/`: `Model.cs`, `Profile.cs` (incl. `ParamsJson`), `ProfileParameters.cs` (`ObservableObject` + alle nullable velden + statische `FromJson`/`ToJson` + const-lijsten voor dropdowns), `Runtime.cs`, `LlamaServerState.cs`.
- [x] `Repositories/Database.cs`: `user_version`-migratie (0→1) + tabellen `Models`, `Profiles`, `Runtimes`, `AppSettings` + seeds (`ModelsDirectory`, `RuntimeDirectory`).
- [x] `Repositories/IModelRepository.cs` + `ModelRepository.cs`: `GetAllAsync()`, `GetByIdAsync(int)`, `UpsertManyAsync(...)`, `DeleteAsync(int)`.
- [x] `Repositories/IProfileRepository.cs` + `ProfileRepository.cs`: `GetAllAsync()`, `GetByModelAsync(int modelId)`, `GetByIdAsync(int)`, `UpsertAsync(Profile)` (ParamsJson doorgeven), `DeleteAsync(int)`.
- [x] `Repositories/IRuntimeRepository.cs` + `RuntimeRepository.cs`: `GetAllAsync()`, `UpsertAsync(Runtime)`, `DeleteAsync(int)`.
- [x] `Repositories/IAppSettingsRepository.cs` + `AppSettingsRepository.cs`: `GetValueAsync(string key)`, `SetAsync(string key, string value)`.
- [x] `Services/ModelScannerService.cs` (scan + quant/mmproj-detectie + upsert).
- [x] `Services/RuntimeScannerService.cs` (scan + backend-heuristiek + upsert).
- [x] `Services/LlamaServerCommandBuilder.cs` (pure static `BuildArgs(Runtime, Model, ProfileParameters, int port)` / `BuildCommandLine`).
- [x] `Services/LlamaServerProcessService.cs` (singleton; Load/Unload met 30 s-wacht, log/state events, `CheckAlive`).
- [x] `Services/ServerHealthService.cs` (`/health` poll).
- [x] `MauiProgram.cs`: registreer alle repos + services. `App.xaml.cs`: `OnExit` → unload draaiende server.

### Fase 1 — Runtimes-scherm
- [x] `ViewModels/RuntimesViewModel.cs` + `Views/RuntimesPage.xaml(.cs)`: "Map kiezen" (FolderPicker), "Scan", tabelbinding, Delete-command.
- [x] `AppShell.xaml`: 4 ShellContents (Overzicht, Modellen, Runtimes, Instellingen) met iconen + routes.

### Fase 2 — Modellen + profielen
- [x] `ViewModels/ModelsViewModel.cs`: modelselectie → `ProfileParameters` laden uit blob (fallback op corrupte blob); scan/kiezen-command; profiel CRUD (Add/Opslaan/Verwijderen met Default-blok; Opslaan = `ToJson` → repo); live command-preview property; "Open Folder" + "Delete" model.
- [x] `Views/ModelsPage.xaml(.cs)`: topbar, modeltabel, profieltabel, rechtsonder-paneel met alle secties + velden (gebonden aan `ProfileParameters`) + read-only command-preview + Opslaan/Verwijderen.
- [x] `Converters/SizeToGbConverter.cs` (+ registratie in `Converters.xaml`).

### Fase 3 — Overzicht (laad/onlaad)
- [x] `ViewModels/OverviewViewModel.cs`: 3 dropdowns (model → profielen cascade → runtime), status-regel, logboek-buffer (max ~2000 regels, `AppendOutput`-patroon uit bestaande `MainViewModel`), Laden/Unload-command, `ServerHealthService`-binding.
- [x] `Views/OverviewPage.xaml(.cs)`: topbar, modelbestanden-tabel, live-logboek, opgeslagen-varianten-tabel ("Add" → Modellen).
- [x] Verwijderen testproces: `MainViewModel.RunHelloAsync`/`HelloExePath`/process-lock en "Run hello.exe" UI; `MainPage` → `OverviewPage` (ShellContent-route bijwerken).
- [x] `Views/AddViewsExtension.cs`: alle nieuwe views/viewmodels registreren (`OverviewViewModel`, `ModelsViewModel`, `RuntimesViewModel` als singleton zodat selecties overleven bij navigeren).

### Fase 4 — Instellingen (placeholder)
- [x] `ViewModels/SettingsViewModel.cs` + `Views/SettingsPage.xaml(.cs)`: placeholder "Instellingen — nog te doen".
- [x] Modellen/Runtimes-schermen lezen mappen uit `AppSettings` (niet hard-coded), zodat ze zonder Instellingen-scherm wel persistent zijn.

### Fase 5 — afwerking
- [x] Consistente NL-teksten, iconen, sectie-stijlen (prototype).
- [x] `README.md` kort bijwerken (wat de app nu doet).
- [x] Commit: implementatie + dit planfile (één commit of per fase, bespreekbaar).

## Verification checklist
> Status 2026-08-17 (Default-agent): build + logische checks geverifieerd; de "Handmatig (Windows)"-items vereisen een draaiende app en een echte `llama-server.exe`/GGUF-bestanden en blijven dus open voor de gebruiker.

- [x] `dotnet build` slaagt (Windows-TF `net10.0-windows10.0.19041.0`) zonder nieuwe warnings (0 warnings, 0 errors).
- [ ] Handmatig (Windows): Runtimes → map kiezen met een echte `llama-server.exe` → verschijnt in tabel met backend "Cuda" (of "CPU"); Delete werkt; na herstart-app is runtime nog in DB.
- [ ] Handmatig: Modellen → scan → GGUF-bestanden verschijnen met juiste Naam/Quant/Grootte; mmproj auto-gekoppeld bij model met mmproj in dezelfde map.
- [ ] Handmatig: profiel "Default" verschijnt per nieuw model; Default kan niet verwijderd worden; ander profiel wél; alle velden + poort overleven een app-herstart (JSON-blob round-trip).
- [ ] Handmatig: Vision-paneel MM-projector toggle (2026-08-21, zie `.alta/plans/2026-08-21-mmproj-toggle-profiel.md`): Aan (auto)/Uit/Andere (bladeren) werkt; effectief-label + preview kloppen; oude profielen (MmprojPath afwezig) tonen default zonder crash.
- [ ] Handmatig: corrupte/verwijderde blob (manueel in DB) → app crasht niet, fallback naar leeg profiel + melding.
- [x] Command-preview toont exacte vlaggen van het voorbeeldprofiel, afgezet tegen de referentie-opdracht (verifieerd via tijdelijk console-project dat `LlamaServerCommandBuilder` + `ProfileParameters` linkt: `MATCH: generated args == expected reference args`, incl. `--temp 1.0`, `--min-p 0.00`, `--presence-penalty 0.0`, `--repeat-penalty 1.0`).
- [ ] Handmatig: Overzicht → model + profiel + runtime selecteren → "Laden" → logboek streamt, status "Running (port 8080)"; browser naar `http://localhost:8080` toont webui (of `/health` 200).
- [ ] Handmatig: "Unload" → `llama-server.exe` stopt (Task Manager), status retour "No runtime loaded…".
- [ ] Handmatig: app afsluiten terwijl server draait → `llama-server.exe` stopt (OnExit-hook).
- [x] Pad met spatie in model-/runtimepad → command preview toont correct escaped pad (verifieerd via console-project: `--model "E:\my models\test model.gguf"`).
- [x] JSON-blob round-trip + corrupte-blob fallback (verifieerd via console-project: alle velden overleven; corrupte JSON → leeg profiel, geen crash).
- [x] `git diff --stat` na implementatie: alleen verwachte bestanden + planfile; `docs/prototype-schermen.txt` ongewijzigd (niet gecommit).

> Afwijking (op verzoek gebruiker, 2026-08-17): `PromptEntry`/`IPromptRepository`/`PromptRepository`/`PromptEntries`-table en de `RunHelloAsync`-testcode zijn **verwijderd** (stonden niet in dit plan). `MainViewModel`/`MainPage` zijn vervangen door `OverviewViewModel`/`OverviewPage`.

## Handoff notes
- Draai de app met `dotnet run --project src/LlamaCppStarterApp -f net10.0-windows10.0.19041.0` (of VS).
- Bestaand patroon volgen: repos hebben eigen `SqliteConnection` per call; VM's gebruiken `[ObservableProperty]`/`[RelayCommand]`; `AppendOutput`/`MainThread.BeginInvokeOnMainThread`-patroon uit `MainViewModel` hergebruiken voor process-events.
- `ProfileParameters` is zowel editor-model (rechtsonder-paneel) als opslagmodel (JSON-blob): één instantie per geselecteerd profiel, niet per wijziging opnieuw alloceren; bij Opslaan serialiseren, bij wissel van profiel de oude instantie loslaten.
- `PromptEntry`/`IPromptRepository`/`PromptEntries`-table blijven onaangetast.
- Niet doen: live metrics (tokens/s, KV-cache), GPU-probe (nvidia-smi), redeneren/vision-head velden, "Gevarensoerde instellingen"-knop, editabele runtime command editor, "Run timemap" tab, hardware-kolom, `--fit`-veld, Instellingen-content. Alleen wat in dit plan staat.
- `docs/prototype-schermen.txt` bevat ongecommitte user-wijzigingen: niet touchen, niet committen.
