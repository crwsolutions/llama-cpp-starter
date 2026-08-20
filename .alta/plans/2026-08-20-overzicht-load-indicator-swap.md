# Overzicht: visuele run-indicator + laden = auto-swap (unload + load)

- Status: Implemented (build-geverifieerd; manuele run-checks open voor de gebruiker)
- Plan file: `.alta/plans/2026-08-20-overzicht-load-indicator-swap.md`
- Created: 2026-08-20
- Task: Maak op het Overzicht-scherm visueel duidelijk dat een model draait (kleur-dot, geen animatie) en laat "Laden" bij een geselecteerd ander model/profiel automatisch unladen + load van de nieuwe selectie (zonder bevestigingsvraag).
- Git: not ignored → commit dit plan met de implementatie (conventie).

## Objective
- **Doel 1 (visueel)**: een directe, rustige kleurindicatie dat er een server draait:
  - status-dot in de topbar (groen = draait/start/stop, grijs = gestopt);
  - accent-BoxView van de Modelstatus-kaart wisselt mee (groen = draait, Primary = gestopt).
  Geen animatie (geen ActivityIndicator/blink).
- **Doel 2 (gedrag)**: "Laden" is altijd enabled (behalve IsBusy) en swapt: draait er al een server met **andere model én/of profiel én/of runtime** → eerst `UnloadAsync()`, daarna load van de geselecteerde combinatie. **Geen bevestigingsvraag.** Zelfde combinatie (model + profiel + runtime) → herlaad (zelfde flow).
- **Non-goals**: geen wijziging in `LlamaServerProcessService.LoadAsync`-contract (service blijft weigeren bij Running/Starting — de swap zit in de VM); geen nieuwe dependencies; geen UI op andere schermen.

## Context and evidence
- `OverviewPage.xaml` topbar: pickers Model/Startprofiel/Runtime + `Laden` (enabled = `!IsBusy`, `IsBusy` via InvertedBoolConverter) + `Unload` (enabled = `IsRunning`). Modelstatus-kaart (card 1) heeft een accent-BoxView met `BackgroundColor="{StaticResource Primary}"`.
- `LlamaServerProcessService.LoadAsync` (ln 130-133): `State is Starting or Running` → `return false` (zwijg). `LoadedSession` record (ln 23): `(Runtime, Model, Parameters, Port, ProcessId)` — **geen profile-id**, dus voor model+profiel-vergelijking in de VM een veld nodig. `Session` = null bij stop.
- `Profile` heeft `Id` + `ParamsJson`; `LoadAsync` in `OverviewViewModel` parseert `ParamsJson` zelf.
- `Colors.xaml`: geen groen/grijs-statuskleuren (bestaan: Primary, Gray300-500, etc.).
- Bestaande converters in `Converters.xaml`: `BoolToVisibilityConverter`, `InvertedBoolConverter` (+ others). Bool→Color converter ontbreekt → nieuwe converter.
- `RefreshStatusAsync` (VM) zet `IsRunning` al op `state is Running or Starting or Stopping` → dot-kleur mag direct op `IsRunning` binden (Starting/Stopping tonen ook "actief" = groen; Stopping is kort, acceptabel).
- `OverviewViewModel` is singleton (`AddViewsExtension.cs`); `LlamaServerProcessService.Session` is public en blijft na app-restart null (server is extern) → bij herstart = "Laden", klopt vanzelf.

## Assumptions and open decisions
- **Beslist (user)**: géén bevestigingsvraag bij swap; "Laad om" toepassing = geselecteerd model ≠ geladen model **of** profiel ≠ geladen profiel (of runtime) — gebruiker schakelt meest via het profiel.
- **Beslist (user)**: geen animatie; groen/rood-punt is de richting → gekozen: groen = actief, **grijs** = gestopt (rood leest als "fout"; Modelstatus-kaart toont "Stopped" al als tekst).
- Aannames: selfde model+profiel+runtime "Laden" = herladen (unload + load), geen disabled-knop; dot + kaart-accent gebruiken dezelfde status (groen/grijs); pickers worden gedurende IsBusy disabled zodat selectie niet verandert tijdens de unload→load-overgang.
- Geen open decisions.

## Design notes
- **Swap in de VM, service ongewijzigd**: `OverviewViewModel.LoadAsync` vangt `session != null` → status-tekst + `await UnloadAsync()` + daarna load. `LlamaServerProcessService` blijft pure (1 server, refuse-if-busy); kleinste wijziging, geen dubbele unload-paden.
- **Swap-detectie** = `SelectedModel.Id != session.Model.Id` **of** `SelectedProfile.Id != session.LoadedProfileId` **of** `SelectedRuntime.Id != session.Runtime.Id`. Profiel-vergelijking vereist `LoadedProfileId` op `LoadedSession` (1 veld toevoegen; record wordt alleen in dit project gemaakt/lezen). Alternatief (ParamsJson-string vergelijken) verworpen: kwetsbaar voor key-volgorde/niet-garandeerde exactheid.
- **Knop-tekst**: computed `LoadButtonText` ("Laden" / "Laad om") via `[NotifyPropertyChangedFor]` op `SelectedModel`/`SelectedProfile`/`SelectedRuntime` + `IsRunning` — geen converter, helder reviewbaar. `LoadCommand` en `SemanticProperties.Hint` blijven bound; hint = "Laad de geselecteerde combinatie (stoppt een draaiende server eerst)".
- **Dot-kleur**: nieuwe `BoolToColorConverter` (true → green, false → gray) met `AppThemeBinding`-vrije hex-kleuren (groen leesbaar in licht+donker; grijs idem) via 2 nieuwe `Colors.xaml`-resources (`StatusActive`, `StatusIdle`). 1 dot in de topbar (voóór de Model-picker) + Modelstatus-kaart-accent binden op dezelfde converter → consistent, geen duplicatie van logica.
- **Race/UX**: `LoadAsync` capturet model/profiel/runtime **vóór** de await's; pickers disabled tijdens `IsBusy` (InvertedBoolConverter, zelfde binding als knoppen) → selectie kan niet mid-swap veranderen. Double-click op Laden → `if (IsBusy) return;`-guard (defensief; knop is al disabled, command-directie kan nog).

## Risks and challenges
- **Langzame swap**: unload duurt max 30 s (POST /exit → wait → kill) → UI moet duidelijk "bezig" zijn (existing ActivityIndicator + disabled pickers + StatusText "Verwisselen: …"). User moet niet denken dat de app hangt; StatusText-tekst dekt dit af.
- **Load-fout na geslaagde unload** (bv. runtime-pad verdwenen): server staat stil, oude is weg. Bestaand gedrag voor Load-fout ("Kon server niet starten (zie logboek)") dekt dit; geen extra rollback (onmogelijk — oude sessie is gestopt).
- **MauiX/bindings**: `LoadButtonText` + nieuwe bindingen moeten compileren (x:DataType staat aan) → build is de check.
- **Grootte/look dot**: 10px dot + margin in topbar-grid (Column 0 vooruitgeschuifd via `2*,2*,3*,Auto` → `Auto,2*,2*,3*,Auto`). Klein risico op omlijning op smalle vensterbreedte → margin/klein houden.

## Implementation checklist
- [x] `Resources/Styles/Colors.xaml`: toevoegen `StatusActive` (groen, bv. `#2E9E4F`) en `StatusIdle` (grijs, bv. `#919191` = Gray400-waarde).
- [x] `Converters/Converters.xaml` (+ `.cs` indien converter in code is): `BoolToColorConverter` (true → `StatusActive`, false → `StatusIdle`); registratie in `MauiProgram.cs` als andere converters daar geregistreerd worden. (Converter staat in `VisibilityConverters.cs` naast de overige; geen `MauiProgram.cs`-registratie nodig — converters worden via `Converters.xaml` merged.)
- [x] `Services/LlamaServerProcessService.cs`: `LoadedSession` record uitbreiden met `int LoadedProfileId` (na `Model`); opstelplaats (ln ~195) vullen met de gebruikte `Profile.Id`. `LoadAsync`-signature uitbreiden met `int profileId` (alleen die ene call site: OverviewViewModel).
- [x] `ViewModels/OverviewViewModel.cs`:
  - [x] computed `LoadButtonText`: `IsRunning && (SelectedModel/Profile/Runtime verschilt van `_processService.Session`)` → `"Laad om"`, anders `"Laden"`; `[NotifyPropertyChangedFor(nameof(LoadButtonText))]` op `SelectedModel`, `SelectedProfile`, `SelectedRuntime`, `IsRunning`.
  - [x] `LoadAsync`: guard `if (IsBusy) return;`; capture `SelectedModel/SelectedProfile/SelectedRuntime` + `profileId`; `if (session != null && (verschilt)) { StatusText = "Verwisselen: draaiende server wordt gestopt…"; await UnloadAsync(); }`; daarna bestaande load-flow (met doorgereken `profileId`); failed-load-status ongewijzigd. (Afwijking, per plan-doel: bij gelijke combinatie wordt "Herladen: draaiende server wordt gestopt…" getoond in plaats van "Verwisselen:…".)
- [x] `Views/OverviewPage.xaml`:
  - [x] topbar-grid: `ColumnDefinitions="Auto,2*,2*,3*,Auto"`; BoxView status-dot (10×10, CornerRadius 5, `BackgroundColor="{Binding IsRunning, Converter={StaticResource BoolToColorConverter}}"`, `SemanticProperties.Description="Modelstatus-indicator"`), margin-rechts 10, VerticalOptions Center.
  - [x] `Laden`-knop: `Text="{Binding LoadButtonText}"`, `SemanticProperties.Hint="Laad de geselecteerde combinatie; een draaiende server wordt eerst gestopt"`.
  - [x] 3 pickers: `IsEnabled="{Binding IsBusy, Converter={StaticResource InvertedBoolConverter}}"` (zelfde pattern als de knoppen).
  - [x] Modelstatus-kaart (card 1): accent-BoxView `BackgroundColor="{Binding IsRunning, Converter={StaticResource BoolToColorConverter}}"` (vervangt `Primary`).
- [x] Commit plan + code samen (NL UI-teksten; code-comments in Engels).

## Verification checklist
- [x] `dotnet build src/LlamaCppStarterApp -f net10.0-windows10.0.19041.0` → 0 warnings / 0 errors (MauiX-check op de nieuwe bindings).
- [ ] Manueel run: gestopt → dot grijs, kaart-accent Primary, knop "Laden", Unload disabled.
- [ ] Model laden → dot groen, kaart-accent groen, knop "Laden" (zelfde selectie).
- [ ] Ander **profiel** selecteren (zelfde model draait) → knop "Laad om"; klikken → "Verwisselen:…" → Unload-fase ziebaar → nieuwe server draait met dat profiel (StatusText port + Modelstatus-kaart "Loaded").
- [ ] Ander **model** selecteren → "Laad om" → swap werkt; runtime-switchen evenzo.
- [ ] During swap: pickers + knoppen disabled (IsBusy), ActivityIndicator zichtbaar; double-click op Laden = geen dubbele operatie.
- [ ] Zelfde model+profiel+runtime "Laden" → herladen (unload + load), geen crash, dot blijft/wordt groen.
- [ ] Unload-knop blijft enabled tijdens Running/Starting/Stopping en disabled na stop; app-uitgang gedrag ongewijzigd (ShutdownServer-pad ongetast).

## Handoff notes
- Kleinste coherent wijziging; raakt: `Colors.xaml`, `Converters.xaml` (+ evt. converter `.cs`/registratie), `LlamaServerProcessService.cs` (record + `LoadAsync`-param), `OverviewViewModel.cs`, `OverviewPage.xaml`.
- `LlamaServerProcessService.LoadAsync` krijgt 1 parameter (`profileId`) → update de enige call site (VM); geen andere callers (grep `LoadAsync(` in ViewModels).
- Dot + Modelstatus-accent binden beide op `IsRunning` via dezelfde converter → één waarheid, geen aparte "IsLoaded"-property.
- Geen test-project in dit repo (per gebruiker) → verificatie is build + manueel run (zie checklist).
- `ModelRuntimeStatusTracker`, `RuntimeMetricPollerService`, `ServerHealthService`, hardware-poll en shutdown-pad blijven ongewijzigd.
