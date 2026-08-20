# Overzicht: dropdown-keuze (model/profiel/runtime) persisten over app-herstart

- Status: Approved
- Plan file: `.alta/plans/2026-08-20-overzicht-dropdown-voorkeur.md`
- Created: 2026-08-20
- Task: De geselecteerde Model/Startprofiel/Runtime-dropdowns op het Overzicht-scherm moeten behouden blijven na app-herstart (zelfde waardes worden getoond).
- Git: not ignored; commit dit plan-bestand mee met de implementatie (conventie AGENTS.md: plans horen gecommit te worden met de bijbehorende implementatie).

## Objective
- Na herstart toont het Overzicht-scherm dezelfde selecties als bij het sluiten van de app: model, startprofiel en runtime.
- Niet-doel: per-model profiel-geheugen (laatste profiel per model onthouden) — alleen de globale "laatste selectie" wordt hersteld. Geen wijziging aan het Laden/Unload-gedrag, geen schema-migratie.

## Context and evidence
- `src/LlamaCppStarterApp/Views/OverviewPage.xaml` (lijnen 15–32): drie `Picker`s, two-way gebonden aan `SelectedModel`/`SelectedProfile`/`SelectedRuntime` via `SelectedItem` + `ItemDisplayBinding`. Er is **geen XAML-wijziging** nodig; de wijziging zit volledig in het ViewModel.
- `src/LlamaCppStarterApp/ViewModels/OverviewViewModel.cs`:
  - `EnsureLoadedAsync()` (lijn 106): laadt Models/Runtimes en kiest altijd `Models[0]` / `Runtimes[0]`; profiel wordt via `OnSelectedModelChanged` → `LoadProfilesAsync()` (lijn 164) geselecteerd met fallback-keten `prevProfileId → IsDefault → FirstOrDefault`.
  - `RefreshAsync()` (lijn 144): herlaadt lijsten bij navigeren terug en herkiest op `SelectedModel?.Id` — selectie overleefdt al navigeren (VM is singleton, zie `AddViewsExtension.cs`/AGENTS.md). Alleen een volledige app-herstart verliest de keuze.
- `src/LlamaCppStarterApp/Repositories/IAppSettingsRepository.cs` + `AppSettingsRepository.cs`: generieke key/value `GetValueAsync`/`SetAsync` (upsert) in de `AppSettings`-tabel — exact hetzelfde patroon als `ModelsDirectory`, `RuntimeDirectory` en `GlobalLaunchDefaults`. Geen migratie nodig.
- ID's zijn stabiel: `Model.Id`/`Runtime.Id`/`Profile.Id` zijn int PK's; profielen horen bij `Profile.ModelId`, dus een bewaard profiel-ID is alleen geldig voor dat ene model.

## Assumptions and open decisions
- Aannahme: één globale "laatste selectie" (3 settings-rows) is wat de gebruiker bedoelt met "dezelfde waardes getoond"; per-model profiel-geheugen is niet gewenst voor deze iteratie.
- Aannahme: als een bewaarde ID niet (meer) bestaat (model gerund/slecht gescand), val dan netjes terug op bestaande default-gedrag (eerste model / eerste runtime / Default-profiel) — geen foutmelding.
- Beslissing (bevestigd door gebruiker, 2026-08-20): **laatste globale keuze** — geen per-model profiel-geheugen.
- Aanneming: bij een null-selectie wordt een lege string bewaard (upsert); er is geen delete-methode op `IAppSettingsRepository`, leeg = "niet hersteld".

## Design notes
- Persisteer in `OverviewViewModel` via `_appSettings` (3 nieuwe const keys, NL-ongeacht, consistent met bestaande key-namen):
  - `OverviewSelectedModelId`, `OverviewSelectedProfileId`, `OverviewSelectedRuntimeId` (waarde = `Id.ToString()`, leeg bij null).
- Opslaan op elke selectie-wijziging (fire-and-forget `_ = _appSettings.SetAsync(...)`) in `OnSelectedModelChanged` + nieuwe `OnSelectedProfileChanged`/`OnSelectedRuntimeChanged` partial hooks → overleeft ook sluiten zonder Laden.
- Herstellen in `EnsureLoadedAsync()` (alleen `_loaded == false`, dus bij app-start/eerste navigate):
  - Model: bewaarde ID matchen in `Models`, anders `Models[0]` (bestaand gedrag).
  - Runtime: bewaarde ID matchen in `Runtimes`, anders `Runtimes[0]` (bestaand gedrag).
  - Profiel: alleen als het model ook daadwerkelijk uit de bewaarde ID is hersteld (anders kan het bewaarde profiel bij een ander model horen); in `LoadProfilesAsync()` de fallback-keten uitbreiden met de bewaarde profiel-ID vóór de `IsDefault`-fallback: `savedProfileId (alleen bij model-match) → prevProfileId → IsDefault → FirstOrDefault`.
- Gekozen voor `AppSettings`-rows in plaats van een nieuw veld/profiel-associatie: smallest coherent change, geen DB-migratie, exact bestaand patroon. Alternatieven (per-model profiel-rows, MAUI `Application.Current.RequestStoragePermissions`/prefs API, profiel met `IsLastUsed`-vlag) verworpen: grotere scope of nieuwe afhankelijkheid.
- `RefreshAsync()` (navigeren terug) wijzigen: het herkies-gedrag daar blijft (in-memory selectie overleeft al); eventueel alleen de `prevModelId`-logica laten zoals hij is.

## Risks and challenges
- Stale-IDs na model-/runtime-scan of mapverwisseling: afgehandeld door fallback (zie design); profiel-restore is extra beveiligd door model-match voorwaarde.
- Picker `SelectedItem` + `ObservableCollection`-vervanging: bestaand gedrag, ongewijzigd; nieuwe `Model?`-instance uit de verse lijst matcht op referentie omdat dezelfde lijst-item wordt gekozen.
- Race: `OnSelectedModelChanged` fire-and-forget `LoadProfilesAsync` + `SetAsync` — bestaand patroon, geen nieuwe concurrentie toegevoegd.
- Geen test-project (per gebruiker): verificatie is build + handmatig.

## Implementation checklist
- [x] `ViewModels/OverviewViewModel.cs`: 3 const settings-keys toevoegen (`OverviewSelectedModelId`, `OverviewSelectedProfileId`, `OverviewSelectedRuntimeId`).
- [x] `EnsureLoadedAsync()`: bewaarde model- en runtime-ID herstellen (fallback naar `[0]`); profiel-herstel alleen bij model-match.
- [x] `LoadProfilesAsync()`: fallback-keten uitbreiden — bewaarde profiel-ID (alleen wanneer model uit bewaarde ID hersteld) vóór `IsDefault`/`FirstOrDefault`.
- [x] `OnSelectedModelChanged` + nieuwe `OnSelectedProfileChanged`/`OnSelectedRuntimeChanged` (CommunityToolkit `[ObservableProperty]` partial hooks): fire-and-forget `_ = _appSettings.SetAsync(key, value?.Id.ToString() ?? string.Empty);`
- [x] Geen wijziging aan `Views/OverviewPage.xaml` of repositories (diff is alleen `OverviewViewModel.cs` + dit plan-bestand).
- [ ] Plan-bestand committen met de implementatie — **geblokkeerd door bestaande kapotte build** (zie Deviation & blocker); niet committen zonder toestemming.

## Deviation & blocker (2026-08-20)
- **Bestaande kapotte build (niet veroorzaakt door deze wijziging):** commit `baaa48c` ("replaced log label by collection view") voegde `Scrolled="OnLogScrolled"` toe aan `OverviewPage.xaml` (lijn 253) maar het code-behind (`OverviewPage.xaml.cs`) die `OnLogScrolled` zou definiëren is nooit gecommit. Gevolg: `dotnet build` faalt op `error MAUIX2014: Event handler 'OnLogScrolled' with correct signature not found in type 'global::LlamaCppStarterApp.Views.OverviewPage'` — reeds bij HEAD (verifieerd: `git show HEAD:...xaml.cs` bevat geen `OnLogScrolled`; ook na `obj`/`bin`-clean rebuild faalt het nog).
- **Effect op deze task:** de MAUIX-source-gen blokkeert de C#-compile → een "0 warnings / 0 errors"-build-verificatie is **onmogelijk** zolang de handler ontbreekt. De wijziging zelf is klein, puur C# in `OverviewViewModel.cs` en volgt bestaande patronen (geen nieuwe API's; `int.TryParse`, `_appSettings.SetAsync/GetValueAsync` bestaan al en worden elders gebruikt).
- **Residu-risico:** geen compile-verificatie van `OverviewViewModel.cs`; code is handmatig nagekeken op syntax/typecorrectheid.
- **Keuze voor gebruiker:** (a) ik voeg de ontbrekende `OnLogScrolled`-handler (smart auto-follow per AGENTS.md: `Scrolled` + `CollectionChanged`) toe aan `OverviewPage.xaml.cs` zodat de build doorloopt en deze wijziging verifieerd kan worden — dan wél een wijziging aan `OverviewPage.xaml.cs` (afwijking van "alleen ViewModel"); of (b) de gebruiker herstelt zijn eigen `baaa48c`-commit (de code-behind stond blijkbaar oncomitted in zijn editor) en ik verifieer daarna. Geen van beide uitgevoerd zonder toestemming.

## Verification checklist
- [ ] `dotnet build src/LlamaCppStarterApp -f net10.0-windows10.0.19041.0` → 0 warnings / 0 errors.
- [ ] Handmatig: `dotnet run --project src/LlamaCppStarterApp -f net10.0-windows10.0.19041.0`; op Overzicht een niet-eerste model + niet-default profiel + niet-eerste runtime selecteren; app sluiten; opnieuw starten; alle 3 dropdowns tonen dezelfde selecties.
- [ ] Handmatig fallback: een (bewaard) model tijdelijk verwijderen uit de map of DB (bv. DB-copy) → app start zonder crash, valt terug op eerste model + Default-profiel + eerste runtime.
- [ ] Navigeren Models → Overzicht: selectie overleeft (bestaand gedrag, geen regressie).
- [ ] Diff-review: alleen `OverviewViewModel.cs` (+ plan-bestand) gewijzigd.

## Handoff notes
- Enkel `src/LlamaCppStarterApp/ViewModels/OverviewViewModel.cs` wijzigen; XAML/repositories blijven ongewijzigd.
- Gebruik de bestaande partial-hook-naming: `partial void OnSelectedModelChanged(Model? value)` bestaat al (lijn 192) — voeg daar de save aan; `OnSelectedProfileChanged`/`OnSelectedRuntimeChanged` nieuw (CommunityToolkit genereert ze bij de `[ObservableProperty]`-properties).
- Bewaar-waarde is plain `Id.ToString()` (leeg bij null) — geen JSON nodig.
- Probeer geen extra abstractie (geen generic "persisted selection"-hulp) — drie kleine blokken in de VM, consistent met `ModelsViewModel`/`RuntimesViewModel`-patroon.
