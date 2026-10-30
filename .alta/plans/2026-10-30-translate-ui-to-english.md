---
title: Translate all UI text from Dutch to English
status: done
created: 2026-10-30
summary: Replace all remaining Dutch UI strings in views, viewmodels, and services with English equivalents.
---

# Translate UI to English

Replace all remaining Dutch UI strings with English across all views (`AppShell.xaml`, `OverviewPage.xaml`, `ModelsPage.xaml`, `RuntimesPage.xaml`, `SettingsPage.xaml`), viewmodels, and the process service. English is already the convention for most of the app; this completes it.

## What changes

### AppShell flyout items

`AppShell.xaml`: `Overzicht` → `Overview`, `Modellen` → `Models`, `Instellingen` → `Settings` (titles also flow to `TitleToGlyphConverter`).

### Overview page

`OverviewPage.xaml`:
- `Startprofiel` → `Profile`
- `Modelstatus` → `Model Status`
- `Modelstatus-indicator` → `Model status indicator`
- `Laad de geselecteerde combinatie…` → `Load the selected combination; a running server is stopped first`
- `Stop de draaiende llama-server` → `Stop the running llama-server`
- `Nog geen output…` → `No output yet…`

`OverviewViewModel.cs`:
- `Fout bij laden:` → `Failed to load:`
- `Selecteer eerst een model.` → `Select a model first.`
- `Selecteer eerst een startprofiel.` → `Select a profile first.`
- `Selecteer eerst een runtime.` → `Select a runtime first.`
- `Kon server niet starten (zie logboek).` → `Could not start server (see log).`

### Models page

`ModelsPage.xaml` — the largest change, including:
- Page title `Modellen` → `Models`
- `Modellenmap scannen` → `Scan Models`
- `Kiezen` → `Browse`
- `Scan de modellenmap op GGUF-bestanden` → `Scan the models folder for GGUF files`
- `Kies een andere modellenmap` → `Choose a different models folder`
- `Modelbestanden` → `Model Files`
- `Fysieke GGUF-bestanden die zijn gevonden…` → `GGUF files found in the models folder or imported from elsewhere.`
- Table headers: `Naam` → `Name`, `Kwantificatie` → `Quant`, `Grootte` → `Size`, `Map openen` → `Open Folder`, `Verwijderen` → `Remove`
- `Laadprofielen` → `Launch Profiles`
- `Nieuw profiel voor het geselecteerde model` → `New profile for the selected model`
- `Basismodel` → `Base Model`
- `Kopiëren` → `Copy`
- `Kopieer dit profiel…` → `Copy this profile (same model, settings, and port)`
- `Profielnaam` → `Profile Name`
- `Standaardwaarden generatie` → `Generation Defaults`
- `Opslaan` → `Save`, `Verwijderen` → `Delete`
- `Verwijder het geselecteerde profiel (Default kan niet)` → `Delete the selected profile (Default cannot be deleted)`
- Field labels/hints: `Spec-type` → `Spec type`, `Spec draft n-max` → `Spec draft n-max`, `Afbeelding-min` → `Image min`, etc.
- Dutch field descriptions → English equivalents

`ModelsViewModel.cs`: all status messages translated, including `Fout bij laden`, `Fout bij bestand kiezen`, `Fout bij scannen`, `Fout bij map kiezen`, `Nieuw profiel … aangemaakt`, `Profiel … opgeslagen`, `Profiel … verwijderd`, `Model … verwijderd`, `Kon map niet openen`, `Het Default-profiel kan niet worden …`, etc.

### Runtimes page

`RuntimesPage.xaml`:
- Page title `Runtimes` stays as-is
- `Map kiezen` → `Browse`
- `Kies de map met lokale llama.cpp builds…` → `Choose the folder with local llama.cpp builds`
- `Geïnstalleerde lokale builds` → `Installed Local Builds`
- Table headers: `Naam` → `Name`, `Backend`, `Status`, `Locatie` → `Location`, `Actie` → `Action`

`RuntimesViewModel.cs`: `Nog geen runtimes gevonden…`, `Fout bij laden`, `Fout bij map kiezen`, `Map niet gevonden…`, `Fout bij scannen`, `Runtime verwijderd uit de database…`

### Settings page

`SettingsPage.xaml`: `Instellingen` → `Settings`, `Instellingen — nog te doen` → `Settings — todo`

### Process service

`LlamaServerProcessService.cs`: `Kon proces niet beëindigen:` → `Could not terminate process:`

## Steps

- [x] 1. Translate all Dutch strings in `AppShell.xaml`
- [x] 2. Translate all Dutch strings in `OverviewPage.xaml`
- [x] 3. Translate Dutch status messages in `OverviewViewModel.cs`
- [x] 4. Translate all Dutch strings in `ModelsPage.xaml`
- [x] 5. Translate Dutch status messages in `ModelsViewModel.cs`
- [x] 6. Translate all Dutch strings in `RuntimesPage.xaml`
- [x] 7. Translate Dutch status messages in `RuntimesViewModel.cs`
- [x] 8. Translate `SettingsPage.xaml`
- [x] 9. Translate the Dutch message in `LlamaServerProcessService.cs`
- [x] 10. Build and verify: `dotnet build src/LlamaCppStarterApp -f net10.0-windows10.0.19041.0`

## How it is verified

- [x] Build succeeds with 0 warnings / 0 errors
- [x] No Dutch UI strings remain (search for `ï`, `ë`, `ü`, `ö`, `å`, `æ`, `ø`, and Dutch words)
- [x] Run the app and verify all screens display in English

## Decisions

| Decision | Choice | Why |
| --- | --- | --- |
| Flyout item titles | Translate all (`Overview`, `Models`, `Settings`) | `Runtimes` is already English; consistency across all items |

> **Open:** Keep `Runtimes` as-is (already English) or change to `Runners` or another term?

## Out of scope

- Code comments, documentation, and commit messages (already English per convention)
- Technical strings (model names, quantization labels, API responses)
- `AGENTS.md` (project instruction document, not UI)
