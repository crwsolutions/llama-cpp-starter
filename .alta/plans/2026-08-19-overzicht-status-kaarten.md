# Plan: Overzicht-scherm (scherm 1) — Modelbestanden/Laadprofielen eruit, 6 status-kaarten erin

- Status: Approved (2026-08-19; gebruiker goedgekeurd + "FYI: /metrics geeft 501 terug wanneer niet ingeschakeld")
- Plan file: `.alta/plans/2026-08-19-overzicht-status-kaarten.md`
- Created: 2026-08-19
- Task: Breng het Overzicht-scherm in lijn met de aangepaste beschrijving van SCHERM 1: geen Modelbestanden/Laadprofielen meer op deze pagina; wel 6 status-kaarten (Modelstatus, Hardware, Stats, Tokens, MTP-tokens, KV-cache) met de gespecificeerde data-bronnen (lokaal status-state, nvidia-smi, `/slots`, `/metrics`) + nieuwe `--metrics`-toggel in de profiel-editor.
- Git: `.alta/plans/` is niet geïgnoreerd → commit dit planfile mét de implementatie. `docs/prototype-schermen.txt` bevat user-eigenwijzigingen; de gebruiker heeft zelf de gecorrigeerde Scherm-1-beschrijving gegeven, dus de Scherm-1-sectie van dat bestand mag op deze tekst bijgewerkt worden (rest van het bestand onaangetast).

## Objective
- **Weg uit het Overzicht**: panelen "Modelbestanden" en "Laadprofielen" (dienen op Scherm 2 te staan; die staan daar al).
- **Erin in het Overzicht**: middenbereik = 6 kaarten (3×2 raster) met de exacte titels en idle-inhoud uit de beschrijving:
  1. **Modelstatus** — "Stopped {model}" (idle) / "Loading {model} + Loading Time" / "Loaded {model} + Loading Time"
  2. **Hardware** — "No loaded model" (idle) / nvidia-smi-GPU-summary
  3. **Stats** — "Active 0/1 | Queued 0\nBusy/decode: 0, 0" (idle-zero's) / live uit `/slots`
  4. **Tokens** — "No runtime" (idle) / rates + totals uit `/metrics`
  5. **MTP-tokens** — "Inactive" (idle óf niet-speculatief) / MTP-rates uit `/metrics`
  6. **KV-cache** — "Used Unknown\nCapacity Unknown" (idle) / live uit `/metrics` (+ `/slots`-fallback)
- **Behouden**: topbar (Model-, Startprofiel- én Runtime-dropdowns, "Modelmap:"-rij, Laden/Unload) en "Live runtime-logboek" onderaan.
- **Nieuw**: `EnableMetrics`-toggel per profiel (default aan) → `--metrics` in de startopdracht (nodig omdat `/metrics` bij llama-server standaard uit staat).
- **Non-goals**: geen live metrics op Scherm 2/3, geen redeneren/vision-velden, geen grafieken, geen test-project. Dit plan tilde de core-plan-scopegrens "live metrics (tokens/s, KV-cache)" alleen op voor de Overzicht-kaarten.

## Context and evidence
- Huidige `Views/OverviewPage.xaml`: topbar (r12–65), **Modelbestanden** (r70–138), logboek (r140–171), **Laadprofielen** (r174–238). `ModelsPage.xaml` heeft dezelfde panelen al (incl. "Add") → verwijdering uit Overzicht is puur UI + VM-afslanking.
- `ViewModels/OverviewViewModel.cs`: `Models`/`Runtimes`/`Profiles`-collections en `ModelsFolder` blijven nodig (dropdowns + label). Weg: `AddProfileAsync`, `OpenFolderAsync`, `DeleteModelAsync`, `DeleteProfileAsync` (+ `ModelsViewModel.PendingNewProfileModelId`/`HandlePendingNewProfileAsync` wordt dan dead code).
- `Services/LlamaServerProcessService.cs`: singleton, één current server; `State`/`Port`/`ModelName`; `LoadAsync(Runtime, Model, ProfileParameters, port)`; `MarkRunning()` via health-polling. Exposeert nog geen `ProcessId`/geladen-runtime → nodig voor GPU-summary.
- `Services/ServerHealthService.cs`: bestaand poll-loop-patroon (StateChanged → StartPolling/StopPolling, `Task.Delay(2 s)`, `HttpClient` 3 s timeout) → hergebruiken voor de metrics-poller.
- `docs/llama-server-help.txt` r569: `--metrics … (default: disabled)`; r573: `--slots … (default: enabled)` → Stats-kaart werkt altijd, Tokens/MTP/KV-cache alleen met `--metrics`.
- **FYI gebruiker (2026-08-19)**: `/metrics` geeft **HTTP 501** terug wanneer het endpoint niet ingeschakeld is. → In de poller: 501 (en elke andere niet-success status) op `/metrics` = "metrics niet beschikbaar" (niet als fout loggen); `/slots`-data + last-known-retentie blijven gelden.
- **Referentieproject** `E:\repos\llama-cpp-windows-manager\src\LocalLlmConsole.App\Services\` (zelfde techstack WPF→MAUI, porteerbaar):
  - `Runtimes/ModelRuntimeStatusTracker.cs` — Loading/Loaded/Fallback + `_loadingStartedAt` (exact de gespecificeerde tracker).
  - `Runtimes/RuntimeMetrics.cs` — `PrometheusSample` + `ParsePrometheus` + `Sum`/`First` (pure static).
  - `Runtimes/RuntimeDashboardService.cs` (24 KB) — `RuntimeSlotSnapshot` (+counter-record), `ParseSlotSnapshot` (laatste/newe `/slots`-formaten, `next_token` object óf array), `RuntimeSlotsLabel` (→ "Active a/c | Queued q\nBusy/decode b"), `RuntimeKvCacheLabel` (→ "Used …\nCapacity …"), `KvCacheUsagePercent`, token-counters (predicted/prompt/MTP-gen/MTP-acc, seconds-variants), `RateLabel`, `TokenCountLabel`, `MtpTokenSummaryLabel`.
  - `Runtimes/RuntimeMetricSummaryTracker.cs` — `Apply(...)`: wall-clock én seconds-based rates (anti-dilutie), last-known-retentie, per-runtime-key state; `RuntimeMetricDisplaySnapshot`.
  - `Runtimes/RuntimeMetricPollerService.cs` — `/slots` + `/metrics` GET's per sessie (hier aangepast naar één current server).
  - `Infrastructure/GpuStatusProbeService.cs` — alleen de **nvidia-smi**-delen gebruiken: `SummaryAsync` (`--query-gpu=index,name,utilization.gpu,temperature.gpu,memory.used,memory.total`) en `SummaryForProcessAsync` (`--query-compute-apps=gpu_uuid,pid` + uuid-match); `--format=csv,noheader,nounits`; elke fout → "Unavailable".
  - `Infrastructure/GpuStatusService.cs` — alleen `FormatNvidiaSmiCsvLine` + `NormalizeMetricSeparators`.
  - `Infrastructure/GpuSummaryCache.cs` (1 KB) — key + 10 s freshness.
- `Models/ProfileParameters.cs`: JSON-blob (`ToJson`/`FromJson`/`TryParse`) → nieuw veld kost **geen DB-migratie**; bestaand initiatiepatroon `public bool? Jinja { get; set; } = true;` (oude blobs zonder key → initializer-waarde).
- `Views/ModelsPage.xaml` r289/458: `CheckBox` + `BoolNullableConverter`-patroon (NoHost/Jinja) → zelfde voor Metrics-toggel.
- Nieuwe beschrijving SCHERM 1 (van gebruiker, 2026-08-19) met "TECHNISCHE NOTITIES — DATA BRONNEN PER CARD" is de waarheid; de bestaande Scherm-1-sectie in `docs/prototype-schermen.txt` (r10–119) is verouderd.

## Assumptions and open decisions
**Opgelost (gebruiker, 2026-08-19):**
- **Metrics-toggel in profiel**: nieuw "Metrics"-vinkvak (per profiel, `EnableMetrics`, **default aan**); `--metrics` alleen wanneer aangevinkt.
- **Topbar**: Runtime-dropdown én "Modelmap:"-rij **blijven** (alleen de twee tabellen verlaten de pagina).
- **Hardware-kaart**: alleen **nvidia-smi** gebruiken; AMD (rocm-smi), Intel Arc (sycl-ls/clinfo) en CPU-probes (wmic/sensors) **niet** implementeren. Geen NVIDIA-GPU of nvidia-smi afwezig → kaart toont "Unavailable".

**Aannames (laag risico, in plan verwerkt):**
1. MTP-tokens-kaart is altijd zichtbaar; inhoud = "Inactive" tenzij het geladen profiel `SpecType` op `draft-*` of `mtp` begint (per technische notitie 5).
2. Hardware-kaart probeert per-PID-gebruik eerst (`--query-compute-apps` + uuid-match op het llama-server-PID) en valt terug op de volledige `--query-gpu`-lijst; geen device-filtering op basis van `--tensor-split` (dat is weightsplit, geen device-selectie).
3. Kaart-inhoud in Engels zoals de spec-voorbeelden ("No loaded model", "No runtime", "Inactive", "Used Unknown"); UI-titels blijven NL.
4. Modelstatus-kaart toont bij Loading én Loaded een "Loading Time" (teller vanaf `_loadingStartedAt`), zoals het referentiepatroon; idle = "Stopped {model}".
5. `EnableMetrics` in het profiel; bestaande Default-profiel-blobs (zonder key) → default aan (initializer).
6. Poll-interval 2 s (gelijk aan health-service); HTTP-timeout 2 s per request; nvidia-smi-probe met 10 s-caché.
7. Met `EnableMetrics` uit maar server draaiend: Tokens/MTP/KV-cache tonen wat `/slots` alleen al oplevert (referentiegedrag), anders "No runtime"-achtige fallback.

## Design notes

### Architectuur (volgt bestaand patroon: services singleton, pure static waar kan)
```
Models/ProfileParameters        + EnableMetrics (bool? = true)
Services/LlamaServerProcessService  + LoadedSession-record (Runtime, Model, Parameters, Port, ProcessId)
                                   + public LoadedSession? Session (set bij LoadAsync, null bij stop)
Services/LlamaServerCommandBuilder  + --metrics (EnableMetrics is not false), na --jinja
Services/RuntimeMetrics.cs        — port (PrometheusSample, ParsePrometheus, Sum/First, IsFinite)
Services/RuntimeDashboardService.cs — port van de nodigde onderdelen (records, ParseSlotSnapshot,
                                       label-helpers, token/MTP-counters, RateLabel/TokenCountLabel)
Services/ModelRuntimeStatusTracker.cs — port (Loading/Loaded/Fallback + loading-time)
Services/GpuSummaryCache.cs       — port (10 s freshness)
Services/GpuStatusProbeService.cs — nvidia-smi-alleen: SummaryAsync + SummaryForProcessAsync
                                   (kleine interne static ProcessRunner: Process + timeout + Kill)
Services/GpuSummaryService.cs     — aangepaste "RuntimeGpuSummaryApplicationService":
                                   Session null → "No loaded model"; anders nvidia-smi
                                   (per-PID eerst, fallback volledige lijst); GpuSummaryCache
Services/RuntimeMetricSummaryTracker.cs — port Apply() met mini-context-record (Parallel, CtxSize)
                                   ipv het referentie-AppSettings
Services/RuntimeMetricPollerService.cs — NIEUW, singleton, ServerHealthService-patroon:
                                   StateChanged(Starting|Running) → poll-loop (2 s), anders stop;
                                   per tick: GET /slots (altijd) + GET /metrics (als
                                   Session.Parameters.EnableMetrics is not false) →
                                   SummaryTracker.Apply → GPU-summary (cache) → event
                                   MetricsUpdated(MetricCardsSnapshot); Idle → reset + lege snapshot
MauiProgram.cs                    — registreer GpuStatusProbeService, GpuSummaryCache,
                                   GpuSummaryService, RuntimeMetricSummaryTracker,
                                   RuntimeMetricPollerService
ViewModels/OverviewViewModel.cs   — kaart-properties + wiring (zieonder); table-command's weg
Views/OverviewPage.xaml           — 6-kaartenraster, tabellen weg
Views/ModelsPage.xaml             — Metrics-CheckBox in "Prestaties & Geheugen"
```
- `ModelRuntimeStatusTracker` wordt in de `OverviewViewModel` (singleton) geconstrueerd — geen DI nodig.
- Alle poll-events naar UI via `MainThread.BeginInvokeOnMainThread` (bestaand `AppendOutput`-patroon).

### Behavior per kaart (idle → actief)
- **Modelstatus**: tracker. `LoadAsync`-start → `StartLoading(modelId, naam, "http://127.0.0.1:{port}"); StateChanged`: Running → `StopLoading(showLoadedDuration: true, …)`; Idle/Stopping → `ClearLoadedStatus()` + fallback "Stopped {SelectedModel}".
- **Hardware**: `GpuSummaryService.SummaryAsync(Session)`: geen sessie → "No loaded model"; met sessie → nvidia-smi `--query-compute-apps=gpu_uuid,pid` matchen op het llama-server-`ProcessId` → uuid's → `--query-gpu=…` voor die uuid's (max 4 rijen); bij falen/leeg → volledige `--query-gpu`-lijst; bij afwezigheid/fout → "Unavailable". 10 s-caché per sessie-key.
- **Stats**: `RuntimeSlotsLabel(samples, slotSnapshot, configuredSlots)` met `configuredSlots` = geladen profiel `Parallel` (default 1); idle → statisch "Active 0/1 | Queued 0\nBusy/decode: 0, 0" (spec-voorbeeld).
- **Tokens/MTP/KV-cache**: `RuntimeMetricSummaryTracker.Apply(key, samples, context, slotSnapshot, mtpSnapshot)` → `Tokens`/`MtpTokens`/`KvCache`-teksten; MTP-gate per aanneme 1; idle → "No runtime" / "Inactive" / "Used Unknown\nCapacity Unknown".
- **Logboek** (onder): onveranderd, incl. "No runtime is loaded for the selected model." statusregel.

### UI (OverviewPage)
- Pagina-grid `Auto,*,Auto` blijft: topbar (ongewijzigd), midden = `UniformGrid` (Rows=2, Columns=3, ColumnSpacing/RowSpacing=12), logboek (ongewijzigd, `*`-rij krijgt de restruimte; kaarten Auto-ish via UniformGrid-rijen).
- Elke kaart: `Border PanelBorder` + bestaande sectiekop (BoxView-balk + `SectionTitle`) + inhoud-`Label` (FontSize 13, `LineBreakMode=CharacterWrap`, `FontFamily=Consolas` voor de numerieke kaarten Stats/Tokens/MTP/KV-cache; Modelstatus/Hardware normaal).
- Bindingen: `ModelStatusText`, `HardwareText`, `StatsText`, `TokensText`, `MtpTokensText`, `KvCacheText` (alleen-observables in de VM; VM is singleton → overleeft navigeren).

### Profiel-editor + command
- `ProfileParameters`: `public bool? EnableMetrics { get; set; } = true;` (JSON-voorwaarts-compatibel; geen migratie).
- `ModelsPage.xaml` sectie "Prestaties & Geheugen": `CheckBox` "Metrics endpoint" + hint `--metrics (standaard uit bij llama-server)`, `IsChecked="{Binding CurrentParameters.EnableMetrics, Converter={StaticResource BoolNullableConverter}}"` (patroon NoHost/Jinja).
- `LlamaServerCommandBuilder.BuildArgs`: `--metrics` toevoegen wanneer `EnableMetrics is not false`, **na `--jinja`** (einde van de argumentenreeks).

## Risks and challenges
- **Port-volume** (~60 KB referentiecode): alleen de onderdelen die de 6 kaarten daadwerkelijk gebruiken porteren; geen WPF-/WSL-specifieke takken, geen AMD/Intel/CPU-probes, geen gateway/OpenAI-endpoints, geen graph-samples meenemen.
- **`/slots`-formatvariatie** (llama.cpp-versies): referentie-parser hanteert al array én object-vorm van `next_token` → meenemen, niet "verbeteren".
- **Metrics-console-check wordt ongeldig**: eerdere `MATCH: generated args == expected reference args`-check verandert (default `--metrics` erbij). Check verwachten op `referentie + --metrics`; óók `EnableMetrics=false → geen --metrics`.
- **Geen NVIDIA-omgeving**: zonder nvidia-smi/GPU toont de Hardware-kaart "Unavailable" (idle blijft "No loaded model"); dat is per gebruiker de gewenste scope (nvidia-smi alleen).
- **Poll-overlap**: 2 s-tick met maximaal 2 sequentiële 2 s-timeouts → tick kan 4 s duren; acceptabel (geen race, alleen vertraagde update). `HttpClient`-timeout 2 s zetten.
- **Bestaande blobs zonder `EnableMetrics`**: initializer `true` → bestaande profielen schakelen `--metrics` na update in (bewuste keuze per gebruiker; vermeldbaar in release-tekst).
- **Dead code**: `PendingNewProfileModelId`/`HandlePendingNewProfileAsync` (ModelsViewModel) en 4 VM-command's verdwijnen → `git diff` moet alleen de verwachte bestanden tonen.

## Implementation checklist
### Fase 1 — fundament (models + process service + command builder)
- [x] `Models/ProfileParameters.cs`: + `public bool? EnableMetrics { get; set; } = true;` (naast `NoHost`/`Jinja`).
- [x] `Services/LlamaServerCommandBuilder.cs`: `--metrics` bij `EnableMetrics is not false`, na `--jinja`.
- [x] `Services/LlamaServerProcessService.cs`: record `LoadedSession(Runtime, Model, ProfileParameters, int Port, int ProcessId)`; public `LoadedSession? Session` (set in `LoadAsync` na succesvolle start; null bij `UnloadAsync` en bij natuurlijke stop via `CheckAlive`/wait-loop).

### Fase 2 — pure-static ports (metrics + status + nvidia-smi)
- [x] `Services/RuntimeMetrics.cs`: port `PrometheusSample`, `ParsePrometheus`, `Sum`, `First`, `IsFinite`, `Matching`/`NormalizeMetricName`.
- [x] `Services/RuntimeDashboardService.cs`: port `RuntimeSlotSnapshot` + `RuntimeSlotCounterSnapshot`, `ParseSlotSnapshot`, `RuntimeSlotsLabel`, `RuntimeKvCacheLabel`, `KvCacheUsagePercent`, `GeneratedTokenCounter`, `PromptTokensProcessedCounter`, `PromptCachedTokenCounter`, `Mtp*TokenCounter`/`Mtp*SecondsCounter`, `RateLabel`, `TokenCountLabel`, `MtpTokenSummaryLabel`, `SumNullable`/`MaxNullable`/`Rate`/`CounterRate`. (Overige referentie-rest uitlaten.)
- [x] `Services/ModelRuntimeStatusTracker.cs`: port 1:1 (namespace aanpassen).
- [x] `Services/GpuSummaryCache.cs`: port 1:1 (10 s freshness).
- [x] `Services/GpuStatusProbeService.cs`: alleen nvidia-smi porteren uit het referentieproject: `SummaryAsync()` en `SummaryForProcessAsync(int processId)` (`--query-gpu=…` / `--query-compute-apps=gpu_uuid,pid`, `--format=csv,noheader,nounits`, 2 s-timeout, elke fout → "Unavailable") + formatter `FormatNvidiaSmiCsvLine` (+ `NormalizeMetricSeparators`) — als kleine pure-static class of in dezelfde file; kleine interne `static ProcessRunner.RunAsync(ProcessStartInfo, timeout)` (CreateNoWindow, stdout capture, `WaitForExitAsync` met timeout → Kill).

### Fase 3 — adaptaties + poller
- [x] `Services/GpuSummaryService.cs`: singleton; `SummaryAsync(LoadedSession? session, CancellationToken)`: null → "No loaded model"; anders cache-check (key = `{modelId}|{port}|{processId}`) → `GpuStatusProbeService.SummaryForProcessAsync(processId)`; bij "Unavailable"/leeg → fallback `SummaryAsync()`; resultaat caché-`Store`. (Referentie-naam `RuntimeGpuSummaryApplicationService` is te breed voor deze nvidia-smi-only scope.)
- [x] `Services/RuntimeMetricSummaryTracker.cs`: port `Apply` + state; `AppSettings`-parameter vervangen door mini-record `RuntimeMetricContext(int ParallelSlots, int? ContextSize)`; `KvUnified` = "auto".
- [x] `Services/RuntimeMetricPollerService.cs`: singleton; `ServerHealthService`-patroon (StateChanged → start/stop, `_pollLock`, `_cts`, 2 s-tick); per tick `GET http://127.0.0.1:{port}/slots` (altijd) en `GET /metrics` (alleen `EnableMetrics is not false`); **niet-success status op `/metrics` (bv. 501 "not enabled") = leeg-lijst, géén log-spam/fout**; → `SummaryTracker.Apply` + `GpuSummaryService.SummaryAsync` → `event MetricsUpdated(MetricCardsSnapshot)`; `MetricCardsSnapshot(string StatsText, string TokensText, string MtpTokensText, string KvCacheText, string HardwareText, bool HasRuntime)`; bij stop: `SummaryTracker.Reset()` + lege snapshot.
- [x] `MauiProgram.cs`: registreer `GpuStatusProbeService`, `GpuSummaryCache`, `GpuSummaryService`, `RuntimeMetricSummaryTracker`, `RuntimeMetricPollerService` (singletons).

### Fase 4 — OverviewViewModel + OverviewPage
- [x] `ViewModels/OverviewViewModel.cs`: + `ModelStatusText`, `HardwareText`, `StatsText`, `TokensText`, `MtpTokensText`, `KvCacheText`; eigen `ModelRuntimeStatusTracker`; wiring: `LoadAsync` → `StartLoading`; `OnServerStateChanged` → tracker + default-herstel bij Stopping/Idle; `RuntimeMetricPollerService.MetricsUpdated` → main-thread update; MTP-gate (`SpecType` start `draft`/`mtp`); idle-defaults exact per spec (zie "Behavior per kaart").
- [x] `ViewModels/OverviewViewModel.cs`: verwijderen `AddProfileAsync`, `OpenFolderAsync`, `DeleteModelAsync`, `DeleteProfileAsync` (+ ontsing `_modelRepository`/`_profileRepository` waar niet meer nodig — `Models`/`Profiles`/`Runtimes`/`ModelsFolder` blijven voor dropdowns/label).
- [x] `ViewModels/ModelsViewModel.cs`: verwijderen `PendingNewProfileModelId` + `HandlePendingNewProfileAsync` (dead code na verdwijning Overzicht-"Add").
- [x] `Views/OverviewPage.xaml`: panelen "Modelbestanden" (r70–138) en "Laadprofielen" (r174–238) verwijderen; midden = `UniformGrid` 3×2 met de 6 kaarten (bestaande sectie-kop-stijl + inhoud-Label per kaart); topbar en logboek ongewijzigd. (Deviatie: MAUI heeft géén `UniformGrid` → `Grid` met `RowDefinitions="*,*"` / `ColumnSpacing/RowSpacing=12`, zelfde 3×2-resultaat.)

### Fase 5 — profiel-editor + docs
- [x] `Views/ModelsPage.xaml`: in "Prestaties & Geheugen" `CheckBox` "Metrics endpoint" (hint: `--metrics`), binding `CurrentParameters.EnableMetrics` via `BoolNullableConverter`.
- [x] `docs/prototype-schermen.txt`: Scherm-1-sectie (r10 t/m vóór SCHERM 2) vervangen door de door de gebruiker gegeven gecorrigeerde beschrijving incl. "TECHNISCHE NOTITIES — DATA BRONNEN PER CARD"; notitie 2 (Hardware) daar op "nvidia-smi alleen" bijstellen; rest van het bestand onaangetast.
- [x] `README.md`: "Overzicht"-bullet bijwerken (6 status-kaarten, Hardware via nvidia-smi, `--metrics`-toggel).
- [x] `AGENTS.md`: services-lijst uitbreiden met de nieuwe services; scope-grenzen-bijwerking (live-metrics-kaarten op Overzicht wél in scope sinds 2026-08-19; GPU-probe = nvidia-smi alleen); `LlamaServerCommandBuilder`-notitie `--metrics` vermelden.

## Verification checklist
- [x] `dotnet build src/LlamaCppStarterApp -f net10.0-windows10.0.19041.0` → 0 warnings / 0 errors (incl. compiled XAML-bindings).
- [x] Handmatig (tijdelijk console-project dat de bronnen linkt; daarna verwijderen):
  - [x] `BuildArgs` met default-parameters (oude blob zonder `EnableMetrics`-key) = referentie-opdracht + `--metrics` (en correcte volgorde: `--metrics` na `--jinja`).
  - [x] `EnableMetrics = false` → geen `--metrics`.
  - [x] `ProfileParameters.TryParse` op oude JSON (zonder key) → `EnableMetrics == true`.
  - [x] `ParsePrometheus` op een voorbeeld-`/metrics`-body → verwachte samples; `ParseSlotSnapshot` op voorbeeld-`/slots`-JSON (zowel array- als object-`next_token`) → verwachte waarden. (Extra: `RuntimeKvCacheLabel`, `RuntimeSlotsLabel`, `ModelRuntimeStatusTracker` Loading/Loaded, `FormatNvidiaSmiCsvLine` — ALL PASSED.)
- [x] `git diff --stat`: alleen verwachte bestanden + planfile; `docs/prototype-schermen.txt` alleen de Scherm-1-sectie gewijzigd.
- [ ] Handmatig (Windows, draaiende app):
  - [ ] Overzicht zonder server: kaarten tonen exact de spec-defaults (Stopped/No loaded model/Active 0/1…/No runtime/Inactive/Used Unknown); Modelbestanden- én Laadprofielen-tabellen zijn weg; topbar en logboek ongewijzigd.
  - [ ] Model laden (Default-profiel): Modelstatus → "Loading …" → "Loaded … + Loading Time"; Stats live via `/slots`; Tokens/MTP/KV-cache live via `/metrics`; Hardware toont nvidia-smi-rijen van de GPU's van het llama-server-proces (of "Unavailable" zonder NVIDIA-GPU); MTP-tokens "Inactive" mits `SpecType` geen `draft-*`/`mtp`.
  - [ ] Metrics-toggel in profiel editor: vinkweg + opslaan → bij volgende load geen `--metrics` (zichtbaar in "Opstarten:"-logregel) → Metrics-kaarten vallen terug op slot-data/fallback.
  - [ ] Unload → alle kaarten terug naar defaults; geen weestproces.
  - [ ] Modellen-scherm: Modelbestanden + Laadprofielen + Add + editor onveranderd werkend.

## Handoff notes
- Draai app: `dotnet run --project src/LlamaCppStarterApp -f net10.0-windows10.0.19041.0`.
- **Bron voor ports**: `E:\repos\llama-cpp-windows-manager\src\LocalLlmConsole.App\Services\` (paden per bestand in "Context and evidence"). Namespace → `LlamaCppStarterApp.Services`; geen externe dependencies; System.Text.Json + System.Diagnostics zijn beschikbaar.
- **Hardware-kaart = nvidia-smi alleen** (gebruikersbeslissing 2026-08-19): geen PowerShell/WMI-scripten, geen sycl-ls/clinfo, geen rocm-smi, geen CPU-load/temperatuur. Alleen `SummaryAsync` + `SummaryForProcessAsync` (incl. uuid-match) uit `GpuStatusProbeService` en de `FormatNvidiaSmiCsvLine`-formatter porteren.
- `RuntimeMetricPollerService` mag letterlijk het `ServerHealthService`-skelet als template gebruiken (StateChanged-abonnement, `_pollLock.Wait(0)`-start/stop, `Task.Delay`-loop) — consistentie boven nieuwe constructies.
- `LlamaServerProcessService.Session` moet ook opgeborgen worden bij **natuurlijke** processtop (de `WaitForExitAsync`-loop en `CheckAlive`), niet alleen bij expliciete unload.
- `--metrics` default-aan is een bewuste wijziging van de referentie-opdracht (gebruikerskeuze 2026-08-19); oude console-check "MATCH: reference args" moet dan verwachten op `referentie + --metrics`.
- Kaart-inhoud Engels (spec-voorbeelden), UI-titels NL; `UniformGrid` (Rows=2, Columns=3) voor het kaartenraster; `CharacterWrap` (geen `TailTrunc` in deze MAUI-versie).
- Geen test-project; pure-static-delen handmatig verifiëren via tijdelijk console-project (patroon uit eerdere plannen), daarna verwijderen.
- `docs/prototype-schermen.txt`: alleen de Scherm-1-sectie aanpassen (gebruikersgegeven tekst, Hardware-notitie aangepast naar nvidia-smi); niet de hele herschrijven.
- Commit: implementatie + dit planfile; `docs/prototype-schermen.txt` alleen meenemen voor de Scherm-1-sectie (overige user-wijzigingen in dat bestand niet committen zonder toestemming).
