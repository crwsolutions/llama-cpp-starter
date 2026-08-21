# Plan — MM-projector toggle in profiel (ModelsPage)

**Status**: **Approved (2026-08-21, gebruiker)** — Default-implementation, geen test-project (conventie), verificatie op de Windows-TF.

## Context

- Het Vision-paneel op `ModelsPage` heeft nu één Entry voor `CurrentParameters.MmprojPath` met de dubbele
  semantiek "null = auto / leeg = uit / pad = override" (placeholder: `auto (null) of leeg = uit`).
  Dat is onduidelijk: de gebruiker wil vooral **aan/uit kunnen zetten bij detectie**; override/specificatie
  is een nice-to-have.
- Gevraagde default: **Aan als mmproj gedetecteerd + aanwezig is, anders Uit** (geen stil "auto" naar
  een niet-bestaand pad).

## Besluit

| # | Keuze |
|---|-------|
| D1 | Toggle hoort in het **profiel** (zoals alle andere startparameters) — niet op het model zelf. Geen nieuw model-veld, **geen DB-migratie**. |
| D2 | `ProfileParameters.MmprojPath` (JSON-blob-veld) en `GetEffectiveMmproj` + `LlamaServerCommandBuilder` **blijven ongewijzigd**: `null` = auto (model-koppeling), `""` = uit, pad = override. Editor-only wijziging; oude blobs blijven compatibel. |
| D3 | Editor = **3-standen Picker** + (optionele) Entry + "Blader…". File picker = **`Microsoft.Maui.Storage.FilePicker` (Essentials)** — reflectie-geverified (2026-08-21): CT.Maui 15.0.0 bevat FilePicker/`FileType`/`FileResult` **niet meer** (alleen FileSaver/FolderPicker; de `FilePicker.Default` in het gebruiker-voorbeeld is het oude CT.Maui-API en compileert niet). Gebruik: `FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Kies mmproj GGUF", FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, string[]> { { DevicePlatform.Windows, [".gguf"] } }) })` → `FileResult?` met `FullPath` + `FileName` (beide bevestigd). **Annuleren = `null` result** (geen `IsSuccessful`). Fallback indien `FullPath` leeg blijft: `FileName` + folder van model. |
| D4 | Picker-opties: **"Aan (auto)"** (null), **"Uit"** (`""`), **"Andere (bladeren)…"** (pad). Selectie "Andere…" opent direct de file picker; annuleren = terugval op de vorige modus (geen half lege staat). Entry blijft handmatig typeerbaar; Picker toont dan "Andere (bladeren)…" (Custom-stand). |
| D5 | **Default-behavior** (gebruikerswens): de **Picker-selectie** die opvalt bij laden van een model + profiel = **Aan (auto)** indien `GetEffectiveMmproj` niet null is, anders **Uit**. De opgeslagen waarde wordt bij bloot tonen **niet** geschreven: alleen een expliciete gebruikerselectie wijzigt `MmprojPath`. Veld leeg (`null`) maar geen mmproj gevonden → Picker toont "Uit" (visual default), toetswaarde blijft `null` tot de gebruiker iets kiest (dus niets onbedoelds wordt weggeschreven bij opslaan). |
| D6 | **Effectief-pad-label** ("Gedetecteerd/effectief") toont exact wat de startopdracht krijgt (`GetEffectiveMmproj` = wat de command-preview laadt): bestandsnaam indien aanwezig, anders `—`. Dit dekt het "welke mmproj wordt er daadwerkelijk gebruikt?"-vraagtekentje (preview en label komen vandaan). De command-preview onderin (`OnParametersChanged` → `UpdateCommandPreview`) toont live of `--mmproj` wél/niet meegaat. |
| D7 | Bestaande patterns volgen: converter in `Converters.xaml`, NL-UI-teksten, Engelse code-comments. `FilePicker` uit `Microsoft.Maui.Storage` (Essentials; zit al in de MAUI global usings, dus géén nieuwe `using`/dependency). Let op: géén verwarring met `CommunityToolkit.Maui.Storage` (daar zit alleen nog `FolderPicker`). |
| D8 | **Non-goal** (bewust): builder op de slimmere `FindVisionProjectors` (family/size-match, f16-prioriteit) veranderen — dat is een aparte gedragswijziging. Optioneel opvolg-idee (alleen noemen, niet doen): builder + label + picker één bron (`FindVisionProjectors`), zodat twee projectors in één map correcte matchen. |

## Bestanden

| Bestand | Verandering |
|---|---|
| `src/LlamaCppStarterApp/Converters/NullableConverters.cs` (of nieuw klein bestand naast) | `MmprojModePickerConverter` toevoegen: `MmprojMode` (Auto/Off/Custom) ↔ Picker-item (index 0/1/2, `null` → index 1 = Uit); `ConvertBack` op `null` = `Custom` (picker-selectie = expliciete actie). |
| `src/LlamaCppStarterApp/Converters/Converters.xaml` | Key registreren (`MmprojModePickerConverter`). |
| `src/LlamaCppStarterApp/ViewModels/ModelsViewModel.cs` | 1) `MmprojMode` (enum, observable) met herleid-logica (D5) op `SelectedModel`/`SelectedProfile`-wissel en model-scan/refresh — **niet** per property-change (R3). 2) `MmprojEffectivePath` (observable string, bestandsnaam) herleiden op model-schakel + `OnParametersChanged` (goedkoop: `GetEffectiveMmproj`-naam; `—` indien null). 3) `[RelayCommand] PickMmprojFileAsync()`: `FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Kies mmproj GGUF", FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, string[]> { { DevicePlatform.Windows, [".gguf"] } }) })` (D3); **`null` = geannuleerd** → terugval op vorige modus (D4); succes → `.gguf`-check op `FileName` (defensief; native filter kan genegeerd worden) anders statusmelding + terugval; dan `CurrentParameters.MmprojPath = result.FullPath` + `MmprojMode = Custom`. 4) Picker-selectie-actie via `partial void OnMmprojModeChanged`: Auto → `null`, Uit → `""`, Custom → `PickMmprojFileAsync()` (D4). 5) Handmatig pad typen in de Entry → `OnParametersChanged` herleidt `MmprojMode` = Custom (alleen de modus herleiden, niet de ingetypte waarde overschrijven). |
| `src/LlamaCppStarterApp/Views/ModelsPage.xaml` | Vision-grid (huidig Grid.Row=4, 3 rijen) → 5 rijen: sectietitel, **MM-projector** (Label + Picker, items uit `MmprojMode`-lijst), **Eigen pad** (Label + Entry + "Blader…" RowButton; Entry `IsVisible` = `MmprojMode == Custom`), **Gedetecteerd/effectief** (Label + Label `MmprojEffectivePath`, FontFamily Consolas, `StringToVisibilityConverter` → "—" indien leeg), bestaande **Afbeelding-min** (Entry `ImageMinTokens`). Placeholder-tekst weg; `MmprojMode`/`MmprojEffectivePath` zijn VM-level (niet op `CurrentParameters`), dus geen `x:DataType`-bindingprobleem met de nested `ProfileParameters`. |
| `.alta/plans/2026-08-17-llama-cpp-starter-core.md` | Checkbox-regel toevoegen in de handmatige-verificatie-lijst (stroom 4). |
| `AGENTS.md` | Architectuur-lijst: `ProfileParameters`-regels a.v. "MmprojPath (string?): null = auto / leeg = uit / pad = override; editor = 3-standen toggle in ModelsViewModel". `LlamaServerCommandBuilder`-regels ongewijzigd (geen wijziging). |

## Stromen

- [x] **1. Converter + VM-logica (kern)** — `MmprojModePickerConverter` + `MmprojModeCustomToVisibilityConverter` (nieuw bestand `Converters/MmprojModeConverters.cs`); `MmprojMode`/`MmprojEffectivePath` + `ComputeMmprojMode`/`SetMmprojMode`/`SyncMmprojEditorState` + `PickMmprojFileAsync` in `ModelsViewModel`; `MmprojMode`-enum in `Models/Profile.cs`. `MmprojPath`-semantiek ongewijzigd. **Afwijkingen t.o.v. oorspronkelijk ontwerp** (vindt gedrag niet): (a) `DevicePlatform` is in MAUI 10 een struct met statische properties → Windows = `DevicePlatform.WinUI` (niet `.Windows`); (b) `previousMode` uit `MmprojMode` was bij annuleren altijd al Custom → nu `previousMode = ComputeMmprojMode()` vóór de wijziging, en programmatieve modus-setjes lopen via `SetMmprojMode` (suppressed) zodat de file-dialog géén onbedoelde trigger wordt bij sync (R3 opgelost).
- [x] **2. XAML Vision-paneel** — 5-rijensgrid + Picker/Entry/Bladeren/effectief-label; `Converters.xaml` keys; gecompileerde bindings checken (x:DataType).
- [x] **3. Build** — `dotnet build src/LlamaCppStarterApp -f net10.0-windows10.0.19041.0 --no-incremental` → 0 warnings / 0 errors (2026-08-21).
- [ ] **4. Handmatig verifiëren** (geen test-project; vereist draaiende app + echte GGUF/mmproj):
  - [ ] Vision-model + mmproj in map → Vision-paneel: Picker = **Aan (auto)**, effectief = bestandsnaam mmproj, command-preview bevat `--mmproj`.
  - [ ] Picker → **Uit** → `--mmproj` verdwijnt uit preview; Effectief = `—`. Opslaan → herbekijken → toets blijft `Uit`.
  - [ ] Picker → **Andere (bladeren)…** → file picker; annuleren → terugval op eerdere modus; kiezen `.gguf` → pad in Entry + preview; niet-`.gguf` → statusmelding, modus ongewijzigd.
  - [ ] Non-vision-model → Vision-paneel blijft verborgen.
  - [ ] Oude profiel-blob (MmprojPath afwezig) → geen crash; Picker toont default per D5; opslaan zonder wijziging schrijft niets nieuws weg.
  - [ ] Overzicht-scherm: model laden met uitgeschakeld mmproj → server start zonder vision (health OK, geen `--mmproj` in log).
- [ ] **5. Opsom** — plan-update + `AGENTS.md` + commit (conventie: plan-bestanden gecommit met implementatie; NL-commitmessage-stijl).

## Risico's / edge-cases

- **R1** — Picker `null`-index-gedrag: gecompileerde bindings kunnen `null` SelectedItem als index 0 interpretatie forceren → mitigatie D5 (converter + VM-koppeling in plaats van direct binden op `MmprojPath`). Als het toch misdraait: `SelectedItem` binden op `MmprojMode` zelf (string-lijst in XAML, converter alleen voor de display-labels).
- **R2** — ~~`FileResult.FullPath` ontbreekt~~ → **opgelost door reflectie-sondage (2026-08-21)**: `Microsoft.Maui.Storage.FileResult.FullPath` bestaat in MAUI 10 (bevestigd); annulering = `null`. Residueel: als `FullPath` bij Windows runtime leeg blijf (onwaarschijnlijk) → fallback `result.FileName` + model-map, of handmatig typen via Entry.
- **R3** — `MmprojMode`-herleiding op `OnParametersChanged` mag de net-gemaakte picker-selectie niet overschrijven (binding-feedback-loop) → guard: alleen herleiden op model-/profiel-schakel en na scan, niet per property-change.
- **R4** — Twee projectors in één map: label + preview tonen de simpele scan-koppeling (eerste `*mmproj*`), niet de slimme match — bewust ongewijzigd (D8); "Andere (bladeren)…" dekt het.

## Openstaande

- Geen. (D8-noot: eventuele volgende iteratie = één bron voor projector-resolutie over de hele app.)
