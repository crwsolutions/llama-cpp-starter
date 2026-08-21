# GPU-summary model + Hardware-kaart met usage-bars

- Status: Approved (2026-08-21; gebruiker keek het plan door en kees "Switch to Default and execute")
- Plan file: `.alta/plans/2026-08-21-gpu-summary-model-bars.md`
- Created: 2026-08-21
- Task: `GpuSummaryService` retourneert een getypeerd GPU-model i.p.v. een string; de Hardware-kaart toont per GPU één regel met tekst + 2 horizontale bars via de ingebouwde MAUI `ProgressBar` (gebruiker: ProgressBar ok, geen chart-library).
- Git: plans zijn niet geïgnoreerd → commit dit plan mee met de implementatie.

## Objective
- Vervang de string-based GPU-summary door een `GpuSummary`-model: **id** ("GPU 0"), **card model**, **gpu usage %**, **temp °C**, **mem usage (GiB)**, **mem available (GiB)**.
- Hardware-kaart (Overzicht, card 2): per GPU **één regel** (gebruiker bevestigd): `GPU 0 · NVIDIA GeForce RTX 5060 Ti | 62% | 58°C | 14.2/16.0 GiB` + **2 horizontale bars**: GPU usage (0–100%) en mem usage (0–totaal).
- Bars via de ingebouwde MAUI **`ProgressBar`** (gebruiker akkoord; geen chart-library, geen custom control).
- Non-goals: geen AMD/Intel/CPU-probes, geen query-wijzigingen, geen nieuwe dependencies, geen test-project.

## Context and evidence
- `Services/GpuStatusProbeService.cs`: `SummaryAsync` (machine, query `index,name,utilization.gpu,temperature.gpu,memory.used,memory.total`, `Take(4)`) en `SummaryForProcessAsync` (uuid-match op PID, `Skip(1)` → zelfde 6-veldenshape); elke fout → `"Unavailable"`-string.
- `Services/GpuStatusService.cs`: `FormatNvidiaSmiCsvLine` bouwt `GPU {i}: {name} | {u}% | {t}C | {used}/{total} GiB`; `NormalizeMetricSeparators`. Enige gebruikers: probe (2×) + `GpuSummaryCache.Store`.
- `Services/GpuSummaryCache.cs`: single-slot cache (key/string/capturedAt, 10 s fresh).
- `Services/GpuSummaryService.cs`: sessie-key `modelId|port|pid`, machine-key `"machine"`; per-PID probe valt terug op machine-lijst bij leeg/`"Unavailable"`. (Doc-comment "never evict each other" is onjuist voor een single-slot cache → meebuigen.)
- `ViewModels/OverviewViewModel.cs`: `HardwareText` (string, default "Unavailable"), 10 s-poll `RefreshHardwareAsync()` met session-reference- en shutdown-guard.
- `Views/OverviewPage.xaml` card 2 (r. 140-162): één `Label` gebonden op `HardwareText` (FontSize 13).
- Bestaande patterns: `DataTemplate x:DataType="models:…"` (ModelsPage r. 84); converters `BoolToVisibilityConverter` + `InvertedBoolToVisibilityConverter` (Converters.xaml); kleuren `StatusActive` #2E9E4F, `Primary` #512BD4, `Gray100` #E1E1E1 (Colors.xaml); Consolas voor metrics-kaarten.
- AGENTS: geen test-project; build moet 0 warnings/0 errors (compiled bindings/MAUIX vangt XAML-bindings); card-content Engels, UI-labels NL, comments EN.

## Assumptions and open decisions
- Gebruiker bevestigd (2026-08-21): **6 properties** (id, naam, usage %, temp, mem used, mem available) — zoals in het plan.
- Gebruiker bevestigd (2026-08-21): **max 4 GPUs** — de huidige `Take(4)`-cap blijft de harde cap; de layout wordt **niet** ontworpen om het aantal GPUs te maximaliseren (géén ScrollView, géén dynamisch gedrag boven 4 rijen).
- **"mem available" = total − used** (geen extra `memory.free` query-veld). Bar-max voor mem = **totaal** (used + available), zodat de bar nooit >100% wordt; interpretatie van "0-mem available" = hele bar = het gehele videogeheugen van de GPU.
- Bar-kleuren (`ProgressColor`): GPU = `StatusActive` (groen), mem = `Primary` (paars); eenvoudig aan te passen bij review.

## Design notes
- **`Models/GpuSummary.cs`** — `public sealed record GpuSummary(string Id, string Name, double? GpuUsagePercent, double? TemperatureCelsius, double? MemoryUsedGb, double? MemoryAvailableGb)` + afgeleide properties (InvariantCulture):
  - display: `DisplayText` ("GPU 0: {name}", alleen id bij lege naam), `UsageText` ("62%" / "—"), `TemperatureText` ("58°C" / "—"), `MemoryText` ("14.2/16.0 GiB" / "—"),
  - bars: `GpuUsageBarValue` en `MemoryUsedBarValue` (non-nullable `double` 0..1 → rechtstreeks `ProgressBar.Value`; 0 bij unknown → geen nullable→double-bindingprobleem), `MemoryTotalGb`.
  - pure static `TryParseParts(string[] parts)`: 6 delen `index,name,util,temp,usedMb,totalMb` → `GpuSummary?`; MB→GiB /1024; `available = max(total−used, 0)`; elk on-parseerbaar getal → null veld (display "—", bar 0); <6 delen → null.
- **`Services/GpuStatusProbeService.cs`** — beide methods retourneren `Task<IReadOnlyList<GpuSummary>>`; zelfde queries/flow/`Take(4)`; fout → lege lijst (vervangt "Unavailable"). Methodenamen blijven (`SummaryAsync`, `SummaryForProcessAsync`) om churn klein te houden.
- **`Services/GpuSummaryCache.cs`** — bewaart `IReadOnlyList<GpuSummary>` i.p.v. string; `Store` roept geen `NormalizeMetricSeparators` meer; `Clear()` → lege lijst.
- **`Services/GpuSummaryService.cs`** — `Task<IReadOnlyList<GpuSummary>> SummaryAsync(...)`; fallback-conditie wordt `sessionSummary.Count > 0`; doc-comment over evictie meebuigen.
- **`Services/GpuStatusService.cs` verwijderen** — na de refactor zijn beide methods dood (parse staat op `GpuSummary.TryParseParts`).
- **Bars = `ProgressBar`** (gebruiker akkoord) — geen custom control, geen extra .cs-bestand: `Value` bindt op de 0..1 fraction-properties, `Minimum=0 Maximum=1`, `HeightRequest 8`, `HorizontalOptions="Fill"`, `IsVisible` niet nodig (value 0 = lege track).
  - Afgekeurd alternatief: eigen Grid+BoxView-bar (eerdere gebruikersvoorkeur) — ProgressBar is inheems, minder code, consistente rendering.
- **`ViewModels/OverviewViewModel.cs`** — `HardwareText` weg; toevoegen `[ObservableProperty] IReadOnlyList<GpuSummary> HardwareGpus` (init leeg) + `[ObservableProperty] bool HardwareUnavailable` (init true = placeholder vóór eerste probe). `RefreshHardwareAsync()`: `var gpus = await _gpuSummary.SummaryAsync(session);` → beide properties zetten, bestaande session/shutdown-guards ongewijzigd; bij uitzondering oude lijst behouden.
- **`Views/OverviewPage.xaml` card 2** — Label vervangen door `VerticalStackLayout` (`VerticalOptions="Start"`):
  - placeholder `Label` "Unavailable", `IsVisible="{Binding HardwareUnavailable, Converter={StaticResource BoolToVisibilityConverter}}"`;
  - rijen-`VerticalStackLayout` `ItemsSource="{Binding HardwareGpus}"` (IsVisible via `InvertedBoolToVisibilityConverter`), `Spacing 10`, `DataTemplate x:DataType="models:GpuSummary"` (toevoegen `xmlns:models`).
  - **één regel per GPU**: `Grid ColumnDefinitions="Auto,Auto,Auto,Auto,*,*"` ColumnSpacing 10: `DisplayText` (`LineOptions="SingleLine"`), `UsageText` / `TemperatureText` / `MemoryText` (Consolas, FontSize 12), `ProgressBar` ×2 (`Value="{Binding GpuUsageBarValue}"` resp. `MemoryUsedBarValue`), elk met `HeightRequest 8`, `Minimum=0 Maximum=1`, `HorizontalOptions="Fill"`, `ProgressColor` resp. `StatusActive`/`Primary`.
  - Kaartstructuur, accent-BoxView en titel "Hardware" blijven.
- **`MauiProgram.cs`** — geen wijzigingen (cache/service blijven singletons).
- **`AGENTS.md`** — updaten: Models (+`GpuSummary`), Services (probe/service/cache → lists; `GpuStatusService` weg), kernmechanismen "Hardware-kaart"-bullet (1 regel/GPU + 2 ProgressBar's).

## Risks and challenges
- Kleine venster: 4 GPU-rijen kunnen de kaart overlopen → `VerticalOptions="Start"`; per gebruikersbeslissing blijft max 4 de harde cap (géén ScrollView/extra GPU-ondersteuning).
- Lange kaartnamen in smalle kaart: `LineOptions="SingleLine"` (AGENTS: géén `TailTrunc`) → naam kan aflopen; acceptabel.
- nvidia-smi "N/A"/ontbrekende velden (zeldzaam) → null → "—" tekst + lege bar (geen crash).
- Compiled XAML-bindings (MAUIX) moeten door de build → `GpuSummary`-properties correct typen (non-nullable double voor `ProgressBar.Value`).
- Record-instancién uit de cache worden gedeeld (immutable → veilig).

## Implementation checklist
- [x] `Models/GpuSummary.cs`: record + display/fraction-properties + `TryParseParts`
- [x] `Services/GpuStatusProbeService.cs`: beide methods parsen naar `IReadOnlyList<GpuSummary>`, lege lijst bij fout
- [x] `Services/GpuSummaryCache.cs`: `IReadOnlyList<GpuSummary>` in plaats van string
- [x] `Services/GpuSummaryService.cs`: list retourneren, empty-list fallback, doc-comment meebuigen
- [x] `Services/GpuStatusService.cs` verwijderen (verifiëren: geen referenties meer)
- [x] `ViewModels/OverviewViewModel.cs`: `HardwareGpus` + `HardwareUnavailable` vervangen `HardwareText`; `RefreshHardwareAsync`
- [x] `Views/OverviewPage.xaml`: card 2 = placeholder + per-GPU regel + 2 bars; xmlns `models` (afbwijking: rijen via `CollectionView` — `VerticalStackLayout` is geen items-container in MAUI — en bars via `Progress` (0..1) i.p.v. `Value`/`Minimum`/`Maximum`, per gebruikersinstructie)
- [x] `AGENTS.md`: architecture-bullets updaten

## Verification checklist
- [x] `dotnet build src/LlamaCppStarterApp -f net10.0-windows10.0.19041.0` → 0 warnings / 0 errors (MAUIX compiled bindings)
- [x] Grep: geen resterende referenties naar `HardwareText` of `GpuStatusService`
- [x] Run: idle → machine-lijst, 1 regel per GPU, bars updaten live (10 s-poll) — door gebruiker bevestigd (2026-08-21: "Everything works as expected")
- [x] Run: model laden → per-PID-rij van de lopende server; unladen → volledige lijst; bars bewegen — idem
- [x] Run: venster hergrooten → bars blijven evenredig — idem

## Handoff notes
- Plan-file samen met de werk committen (`.alta/plans/` niet geïgnoreerd).
- nvidia-smi query-strings en `Take(4)`-cap niet aanpassen.
- Card-content blijft Engels; comments EN; geen nieuwe dependencies.
- Geen test-project; `GpuSummary.TryParseParts` is pure static → eventueel handmatig checken (bv. tijdelijk console-project), net als `LlamaServerCommandBuilder`-wijzigingen.
