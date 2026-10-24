# Translating DBD Reshade

Thanks for helping! Translations live in this folder, one JSON file per language.
The app downloads them from here at every start, so **your fix reaches every user without an app update.**

| File | Language | Status |
|---|---|---|
| `en.json` | English | reference |
| `it.json` | Italiano | ✅ |
| `es.json` | Español | ✅ |
| `fr.json` | Français | ✅ |
| `de.json` | Deutsch | ✅ |
| `pt.json` | Português | ✅ |
| `ru.json` | Русский | ✅ |
| `zh.json` | 简体中文 | ✅ |
| `ja.json` | 日本語 | ✅ |
| `ko.json` | 한국어 | ✅ |

The first translations were machine-assisted: native speakers checking them is the most useful
contribution right now.

## Fix a translation (no tools needed)

1. Open the language file above (e.g. `it.json`) and click the ✏️ pencil icon on GitHub.
2. Change the text on the right of the `:`. Never change the key on the left.
3. Click **Commit changes… → Propose changes** and open the pull request.

## Add a new language

1. Copy `en.json` to `<code>.json`, using the two-letter language code: `pl.json`, `tr.json`, `uk.json`…
   (use `pt-br.json` style only for a regional variant).
2. Set `"_language"` to the language's own name: `"Polski"`, `"Türkçe"`.
3. Translate the values and delete the `"_comment"` line.
4. Open a pull request. Partial translations are welcome: missing texts are shown in English and the
   app shows how complete the language is (e.g. `Polski · 80%`).

## Rules

- **Keep placeholders exactly:** `{0}`, `{1}` are replaced by names and numbers.
  `"Preset \"{0}\" saved!"` → `"Preset \"{0}\" salvato!"`. You may move them within the sentence.
- **`\n` is a line break.** Keep `\n\n` where English has it: it separates paragraphs in dialogs.
- **Quotes inside a text** must be written `\"` — or use your language's quotes: « », „ “, 「」.
- **UPPERCASE keys** in English (e.g. `"Inst.Game": "GAME"`) are section titles: keep them short and
  uppercase if your script has case.
- **Keep it short** for buttons and the sidebar (`Nav.*`, `Set.Clear`…): space is limited.
- Don't translate product names: *Dead by Daylight*, *ReShade*, *GitHub*, *Discord*.
- The keyboard key **Home** (`Inst.Done`) should be named as it appears on keyboards in your country.

## Checking your file

Every pull request runs `tools/validate.py` automatically. A red ❌ tells you exactly
which line to fix (invalid JSON, a missing `{0}`…). To run it yourself:

```
python tools/validate.py --translations
```

Can't use GitHub? Send the translated file on [Discord](https://discord.gg/mC7Eabu3QW) and it will be added for you.
