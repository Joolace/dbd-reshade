# DBD Reshade (WPF)

ReShade installer and preset manager for *Dead by Daylight*, rebuilt as a native Windows app
(C# · WPF · .NET 8). It replaces the PowerShell script with a single `DBDReshade.exe`.

## Features

- **Install Preset** — finds Dead by Daylight (Steam, Epic and Xbox app, or pick the folder by hand), installs
  ReShade automatically if missing, copies the preset and sets `PresetPath` in `ReShade.ini`.
  Searchable preset list, description, source link and a full-screen preview (click or Space).
- **Try on maps** — see a preset on real map screenshots with ReShade, without starting the game
  (formerly the separate D3Daylight app, now built in), or on your own screenshots.
- **Preset Editor** — create a preset in ReShade, live on map screenshots, then save it to the library
  with description, source and the screenshot taken in the editor.
- **Preset Manager** — add your own `.ini` with description, source link and screenshot; delete presets.
- **Copy Presets** — copy any selection of presets to a folder.
- **Settings** — light / dark / system theme and interface language (English, Italiano, Español,
  Français, Deutsch, Português, Русский, 简体中文, 日本語, 한국어), applied instantly. Translations are
  updated from GitHub and anyone can contribute.
- **Storage cleanup** — free the space used by cached previews, old logs and temporary downloads
  (your own presets and screenshots are never touched).
- **Start-up screen** — logo, name and version while the app loads its data and checks for updates
  (at most 3 seconds, so it never hangs when offline).
- **Own title bar** — the main window and dialogs draw their own title bar in the theme colors
  (WindowChrome keeps dragging, snapping and double-click to maximize).
- **Report a bug** — opens Discord and the logs folder, so today's log can be attached.
- **Automatic updates** — new and changed presets are pulled from this repository at startup, and
  **Update now** installs a new release with one click (installer or portable exe, whichever is in use).
- **Installer or portable** — every release has `DBDReshade-Setup.exe` (Start menu, optional desktop icon,
  uninstall) and the portable `DBDReshade.exe`.

Preview images (≈1 GB in the repository) are downloaded one at a time, only when you select a preset,
and then cached.

## Where data lives

Everything the app writes is in `%LOCALAPPDATA%\DBDReshade`:

| Path | Content |
|---|---|
| `Presets\` | preset `.ini` files (the bundled ones are unpacked on first run) |
| `Previews\` | cached preview images |
| `presets.json` | metadata mirrored from GitHub (overwritten on every sync) |
| `user-presets.json` | metadata for presets you added (never overwritten) |
| `settings.json` | theme, language, last game, custom preset folder, presets you deleted |
| `Maps\` | map screenshots for the preview, downloaded one map at a time |
| `Viewer\` | the preview copy of the app with its own ReShade (see below) |
| `My Screenshots\` | the user's own screenshots for the preview and the editor (never cleaned up) |
| `Editor\` | presets being created in the editor, and their screenshots (never cleaned up) |
| `Translations\` | translations downloaded from GitHub |
| `Logs\` | one log file per day |

## Building

Requirements: Visual Studio 2022 (17.8+) with the **.NET desktop development** workload.

1. Open `DBDReshade.sln`.
2. Press **F5** (Visual Studio will ask to restart elevated, because the app requires administrator rights).

To create the distributable exe (self-contained, no .NET runtime needed on the user's PC):

```powershell
dotnet publish src/DBDReshade/DBDReshade.csproj -c Release -o publish
```

The GitHub Actions workflow does the same on every push, and attaches `DBDReshade.exe` to a GitHub
release when you push a tag such as `v2.0.0`.

## Project layout

```
src/DBDReshade/
  Models/        data types (GameInstallation, Preset, settings…)
  Services/      game detection, ReShade install, GitHub sync, preset library
  ViewModels/    one view model per page (MVVM, CommunityToolkit.Mvvm)
  Views/         XAML windows and pages
  Themes/        Colors.Dark/Light.xaml (palettes) + Controls.xaml (styles)
  Strings/       built-in translations, one JSON per language (en.json is the fallback)
  Seed/seed.zip  presets + presets.json bundled into the exe
```

To refresh the bundled presets, rebuild `Seed/seed.zip` from the repository's `Presets/` folder and
`media/presets.json` (zip layout: `Presets/*.ini` and `presets.json` at the root).

## Releasing a new version

1. Raise `<Version>` in `src/DBDReshade/DBDReshade.csproj` (e.g. `2.0.1`) and commit on `wpf-app`.
2. Tag that commit `v2.0.1` (same number) and push the tag. In GitHub Desktop: History → right-click the
   commit → Create Tag → Push origin.
3. The **Build app** workflow builds `DBDReshade.exe` and `DBDReshade-Setup.exe` and creates the release
   with both. It stops with an error if the tag and `<Version>` differ.

At the next start every user sees **Update available**. **Update now** then:

- installed copies: downloads `DBDReshade-Setup.exe` and runs it silently; Setup closes the app,
  updates it and starts it again;
- portable copies: downloads `DBDReshade.exe`, swaps it in place of the running exe and restarts
  (the previous exe, renamed `.old`, is deleted on the next start).

The installer script is `installer/DBDReshade.iss` (Inno Setup 6). Uninstalling asks whether to also
delete `%LOCALAPPDATA%\DBDReshade`.

## Branches

- **`wpf-app`** — this app. Presets, translations and map screenshots are read from this branch, and
  releases of the app are tagged on it.
- **`main`** — the old PowerShell script, left as it is.

To test another branch (e.g. a pull request) set the environment variable `DBDRESHADE_BRANCH` to its
name before starting the app; the log then shows "Test mode".

## Map preview (formerly D3Daylight)

`DBDReshade.exe` has two modes. Started normally it shows the app; started as
`DBDReshade.exe --viewer "<folder>" --title "<map>"` it opens only a Direct3D 11 window that shows the
screenshots in that folder (← → change image, F11 full screen, F1 key help, ESC close; Home opens ReShade
and Print Screen takes a screenshot). The app passes the translated key help (`--keys`) and the theme
(`--theme dark|light`); the window's title bar is tinted with the theme colors through DWM.

ReShade has to sit next to the exe it hooks, and it must never hook the app's own interface. So when
you press **Try on maps** the app:

1. copies itself to `%LOCALAPPDATA%\DBDReshade\Viewer\DBDReshade Preview.exe` (only when it changed);
2. installs ReShade there, once, with the same setup used for the game;
3. writes that `ReShade.ini`: `PresetPath` = the selected preset, and the **game's** `EffectSearchPaths` /
   `TextureSearchPaths`, so the preview uses exactly the same shaders as the game;
4. downloads the chosen map's screenshots and starts the copy in `--viewer` mode.

Screenshots live in `Maps/<Realm name>/` in the repository (one folder per realm, `.png` or `.jpg`).
Add or replace screenshots there and users get them the next time they open that realm.

Trying a preset uses a temporary copy of it in ReShade's performance mode, so it can never change the
preset in the library.

## Preset Editor

ReShade itself is the editor; the app wraps it in four steps:

1. **Name and starting point** — an empty preset or a copy of any preset in the library. The work file is
   `Editor\<name>.ini`; reopening the same name continues the draft.
2. **Screenshots** — a realm, or "My screenshots".
3. **Edit** — the preview opens on the draft with performance mode off. The user presses Home, enables
   effects and moves sliders; ReShade saves every change into the draft. Print Screen saves screenshots to
   `Editor\Screenshots`.
4. **Save** — description, source and preview (the last screenshot of the session is picked
   automatically), then the draft is added to the library as the user's own preset.

## Publishing presets (no app update needed)

The app syncs with the `wpf-app` branch of this repository at every startup (and with **Update presets**),
so adding or changing a preset only needs a commit:

1. Put the `.ini` in `Presets/`.
2. Put its screenshot in `media/preview-preset/` (JPG recommended, ~2K is plenty).
3. Add an entry to `media/presets.json` — the key is the exact `.ini` file name:

   ```json
   "MyPreset.ini": {
       "description": "What the preset looks like / is for.",
       "videoLink": "https://link-to-the-source",
       "preset-preview": "MyPreset.jpg",
       "author": "Creator name",
       "map": "Coldwind Farm",
       "tags": ["dark", "competitive"]
   }
   ```
   `author`, `map` and `tags` are optional and feed the filters on the Install Preset page:
   `map` should be a realm folder name from `Maps/` (leave it out for presets made for every map), and
   `tags` can use `bright`, `dark`, `competitive`, `cinematic` (other words work too, shown as written).
4. Commit and push to `wpf-app`. Presets that arrive this way get a **New** badge for 7 days, and the app
   says how many arrived. Previews over 800 KB are compressed automatically by
   `.github/workflows/compress-previews.yml` (max 1920 px wide, JPG quality 85; a PNG becomes a JPG and
   `presets.json` is updated). To compress the existing ones once: Actions → Compress previews → Run
   workflow with "all".

What users get on the next start:

| Change on GitHub | What the app does |
|---|---|
| new `.ini` | downloads it |
| edited `.ini` (same name) | re-downloads it (files are compared by git SHA) |
| new / edited text in `presets.json` | uses the new description and source right away |
| new screenshot | downloads it the first time the preset is opened |
| edited screenshot (same name) | drops the cached copy, re-downloads it on next view |

Things to keep in mind:

- Presets a user **deleted** are not downloaded again until they press **Restore deleted presets** in
  the Preset Manager.
- If a user saves their own preset with the same name as an official one, the app asks whether to keep
  it as a separate copy (default, so the official one keeps updating) or replace the official one.
  Deleting their version brings the official preset back.
- Removing a preset from the repository does not delete it from users' PCs.
- Sync uses 3 GitHub API calls per run; the anonymous limit is 60 per hour per IP, so it's fine.
  `raw.githubusercontent.com` can cache files for a few minutes after a push.
- `presets.json` tolerates trailing commas and `//` comments. If it is still invalid, the app keeps its
  previous descriptions but still downloads new/updated presets. To catch mistakes before users do, add
  `tools/validate.py` and `.github/workflows/validate.yml` to the presets repository
  (run `python tools/validate.py` locally before pushing; it checks the translations too).

## License

GPL — see the main repository.

## Translations

Texts are JSON files (`{ "key": "text" }`). The app uses, in order:

1. `translations/<code>.json` downloaded from the `dbd-reshade` repository at startup — so translation
   fixes and **new languages** reach users without an app update;
2. `Strings/<code>.json` built into the exe (offline fallback);
3. English for any key still missing.

Contributors edit `translations/` in the `dbd-reshade` repository (see `translations/README.md` there);
"Help translate" in Settings opens that guide. When a release is made, copy `translations/*.json` into
`src/DBDReshade/Strings/` so the built-in copy is current too.

To add a new key: add it to `en.json` (and the other languages), then use `{DynamicResource Key}` in XAML
or `Loc.Get("Key")` in code.

Chinese, Japanese and Korean use the Windows fonts made for them (Microsoft YaHei, Yu Gothic, Malgun
Gothic), since Montserrat only covers Latin and Cyrillic.

The `videoLink` field in `presets.json` is shown as **Source** in the app; the JSON key is unchanged so
existing metadata keeps working.
