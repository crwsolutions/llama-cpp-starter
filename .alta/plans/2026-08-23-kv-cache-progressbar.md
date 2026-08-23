# KV-cache-kaart met ProgressBar (context-gebruik)

- Status: Approved (2026-08-23; gebruiker keek het plan door en kees "Switch to Default and execute")
- Plan file: `.alta/plans/2026-08-23-kv-cache-progressbar.md`
- Created: 2026-08-23
- Task: Eén horizontale ProgressBar in de KV-cache-kaart (Overzicht, card 6) die het deel van de beschikbare context toont dat in gebruik is; de data komt uit de reeds berekende percent-waarde in `RuntimeMetricSummaryTracker`.
- Git: plans zijn niet geïgnoreerd → commit dit plan mee met de implementatie.

## Objective
- ProgressBar in de KV-cache-kaart: gevulde fractie = gebruikte KV-cache-tokens / context-capacity — exact dezelfde percentage die al in de kaart-tekst staat (bv. "Used 12.345 t | 42,1%").
- Idle (geen sessie) / percent niet berekenbaar → lege bar (0) —zelfde conventie als de Hardware-kaart-bars (`GpuSummary`: 0 = unknown/empty).
- Non-goals: geen wijziging van de kaart-tekst, geen nieuwe data-bron (geen extra endpoint/query), geen per-slot bars, geen kleurwissel op drempelwaarden.

## Context and evidence
- `Views/OverviewPage.xaml` card 6 (r. 287–310): `Label` gebonden op `KvCacheText` (Consolas 13), kaart = `Grid RowDefinitions="Auto,*"`, label `VerticalOptions="Center"` met `Margin="0,10,0,0"`.
- `Services/RuntimeMetricSummaryTracker.cs` r. 115–125: `kvUsagePercent` wordt **al** berekend via `RuntimeDashboardService.KvCacheUsagePercent(kvUsage, kvTokens, contextCapacityTokens)` (0..100, null indien niet berekenbaar) — vandaag alleen gebruikt voor de `RuntimeKvCacheLabel`-tekst.
- `Services/RuntimeMetricPollerService.cs`: `MetricCardsSnapshot(StatsText, TokensText, MtpTokensText, KvCacheText)` → `MetricsUpdated`-event → `OverviewViewModel.OnMetricsUpdated` (gemarshall naar main thread); `StopPolling` verstuurt een lege stop-snapshot.
- `ViewModels/OverviewViewModel.cs`: `KvCacheText`-observable; idle default `IdleKvCacheText = "Used Unknown\nCapacity Unknown"`; `UpdateStatusCards()` zet de idle-teksten zonder sessie.
- Op te volgen patroon: `Models/GpuSummary.cs` (`GpuUsageBarValue`/`MemoryUsedBarValue`: non-nullable `double` 0..1, `Math.Clamp`, 0 = unknown) + Hardware-kaart-XAML (`ProgressBar` `HeightRequest="8"`, `HorizontalOptions="Fill"`, `ProgressColor` `StatusActive`/`Primary`).
- Capacity-aggregatie is al multi-slot-aware: `ContextCapacityTokens` = som van per-slot `n_ctx` (`RuntimeDashboardService.ParseSlotSnapshot` r. 104); percent = som gebruikte tokens / som capacity.
- AGENTS.md: build moet 0 warnings/0 errors zijn (compiled XAML bindings); card-content Engels, comments EN; geen test-project.

## Assumptions and open decisions
- **Plaatsing (voorstel)**: bar direct **onder** de Used/Capacity-tekst, over de volle kaartbreedte, 8 px hoog (zie Design notes) — te bevestigen bij review.
- **Bar-kleur (beslist 2026-08-23)**: `StatusActive` (groen, zelfde "usage %"-semantiek als de GPU-usage bar in de Hardware-kaart).
- **Last-known retention**: als de tracker op het vorige snapshot terugvalt (server kort onbereikbaar), houdt de bar de vorige waarde — consistent met de kaart-tekst; bij unload/stopping zet de stop-snapshot de bar op 0.
- **Multi-slot**: één aggregate-bar (som tokens / som capacity); geen per-slot bars (kaart te smal, te veel ruis).

## Design notes
- Gekozen aanpak: de **reeds berekende** percent-waarde exposeren als bar-value; geen nieuw parsen/endpoint.
  - `Services/RuntimeMetricSummaryTracker.cs`: `RuntimeMetricSummaryResult` krijgt `double? KvCacheUsagePercent` (de bestaande lokale `kvUsagePercent`); ook meenemen in `RuntimeMetricDisplaySnapshot` + `Remember(...)`, en in de last-known early-return path de vorige waarde teruggeven (zelfde retention als de tekst).
  - `Services/RuntimeMetricPollerService.cs`: `MetricCardsSnapshot` krijgt `double KvCacheBarValue` (0..1); `TickAsync`: `summary.KvCacheUsagePercent is double p ? Math.Clamp(p / 100.0, 0, 1) : 0`; de stop-snapshot in `StopPolling` → 0.
  - `ViewModels/OverviewViewModel.cs`: `[ObservableProperty] double KvCacheBarValue` (init 0); zetten in `OnMetricsUpdated` (dekt run + lege stop-snapshot) en in de idle-path van `UpdateStatusCards()` → 0.
  - `Views/OverviewPage.xaml` card 6: label vervangen door `VerticalStackLayout` (`Spacing="10"`, `Margin="0,10,0,0"`, `VerticalOptions="Center"`) met daarin het bestaande `KvCacheText`-Label + de nieuwe `ProgressBar` (`Progress="{Binding KvCacheBarValue}"`, `HeightRequest="8"`, `HorizontalOptions="Fill"`, `ProgressColor="{StaticResource StatusActive}"`). Content-blok blijft verticaal gecentreerd in de kaart.
- Afgekeurde alternatieven: bar boven de tekst (duwt de tekst naar beneden, oogt onbalans) of een bar per slot (visuele ruis in een ⅓-brede kaart).

## Risks and challenges
- Kaart is smal/klein: 2-regelige label + 8 px bar past wel, maar de kaart wordt iets voller; de overige 5 kaarten hebben dezelfde grid-hoogte → acceptabel.
- Percent null (metrics uit én geen /slots-capacity) → bar blijft 0 terwijl tekst "Used X t" toont (capacity "Unknown") — acceptabel en consistent met de tekst.
- Compiled XAML bindings (MAUIX) moeten compileren → non-nullable `double`-property in de VM (GpuSummary-patroon).

## Implementation checklist
- [x] `Services/RuntimeMetricSummaryTracker.cs`: `KvCacheUsagePercent` in `RuntimeMetricSummaryResult` + `RuntimeMetricDisplaySnapshot` + `Remember(...)` + last-known path
- [x] `Services/RuntimeMetricPollerService.cs`: `KvCacheBarValue` in `MetricCardsSnapshot` (+ stop-snapshot → 0)
- [x] `ViewModels/OverviewViewModel.cs`: `KvCacheBarValue`-observable (init 0); zetten in `OnMetricsUpdated` + idle-path van `UpdateStatusCards`
- [x] `Views/OverviewPage.xaml`: card 6 = label + ProgressBar (onder de tekst, volle breedte, 8 px)
- [x] `AGENTS.md`: architecture-bullets updaten (Services: snapshot met KvCacheBarValue; Overzicht: KV-cache-kaart met bar)

## Verification checklist
- [x] `dotnet build src/LlamaCppStarterApp -f net10.0-windows10.0.19041.0` → 0 warnings / 0 errors
- [ ] Run: idle → KV-cache-kaart toont idle-tekst, bar leeg
- [ ] Run: model laden + (lange) prompt → bar vult mee samen met de "%" in de tekst; unload → bar weer op 0
- [ ] Run: profiel met metrics uitgeschakeld → bar vult alsnog (via /slots tokens + capacity)

## Handoff notes
- Plan-file meecommitten met de werk (`.alta/plans/` niet geïgnoreerd).
- De percent-berekening in `RuntimeDashboardService.KvCacheUsagePercent` blijft ongewijzigd — alleen exposeren.
- Card-content blijft Engels, comments EN, geen nieuwe dependencies, geen test-project.
