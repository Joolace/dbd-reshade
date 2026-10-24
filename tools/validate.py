#!/usr/bin/env python3
"""
Checks the dbd-reshade repository data before it reaches users.

    python tools/validate.py                  # everything
    python tools/validate.py --presets        # only media/presets.json
    python tools/validate.py --translations   # only translations/*.json

Run from anywhere; paths are relative to the repository root.
Exit code 0 = OK (warnings allowed), 1 = errors found.

presets.json   valid JSON, no duplicate keys, every key is a file in Presets/ (same case), every
               preset-preview exists in media/preview-preset/, videoLink is http(s), no unknown fields.
translations   valid JSON, "_language" set, placeholders {0}/{1} identical to en.json, missing/unknown keys.
"""
import argparse
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PRESETS_JSON = os.path.join(ROOT, "media", "presets.json")
PRESETS_DIR = os.path.join(ROOT, "Presets")
PREVIEWS_DIR = os.path.join(ROOT, "media", "preview-preset")
TRANSLATIONS_DIR = os.path.join(ROOT, "translations")

PRESET_FIELDS = {"description", "videoLink", "preset-preview"}
PLACEHOLDER = re.compile(r"\{\d+\}")
LANG_CODE = re.compile(r"^[a-z]{2,3}(-[a-z0-9]{2,8})?$")


class Report:
    def __init__(self):
        self.errors, self.warnings = [], []

    def error(self, msg):
        self.errors.append(msg)

    def warn(self, msg):
        self.warnings.append(msg)


def load_json(path, label, report):
    """Strict JSON (what GitHub stores), reporting duplicate keys and the exact error position."""

    def no_duplicates(pairs):
        seen = {}
        for key, value in pairs:
            if key in seen:
                report.error(f'{label}: duplicate key "{key}" (the second one silently replaces the first)')
            seen[key] = value
        return seen

    try:
        with open(path, encoding="utf-8-sig") as f:
            data = json.load(f, object_pairs_hook=no_duplicates)
    except FileNotFoundError:
        report.error(f"{label}: file not found")
        return None
    except json.JSONDecodeError as e:
        report.error(f"{label}: not valid JSON: {e.msg} at line {e.lineno}, column {e.colno}")
        return None
    if not isinstance(data, dict):
        report.error(f"{label}: must be a JSON object {{ ... }}")
        return None
    return data


def files_in(folder):
    """Lower-case name -> real name, to catch upper/lower-case mismatches (GitHub URLs are case-sensitive)."""
    return {f.lower(): f for f in os.listdir(folder)} if os.path.isdir(folder) else {}


# ---------------------------------------------------------------------------------------------------
# presets.json
# ---------------------------------------------------------------------------------------------------

def check_presets(report):
    data = load_json(PRESETS_JSON, "presets.json", report)
    if data is None:
        return

    presets = files_in(PRESETS_DIR)
    previews = files_in(PREVIEWS_DIR)

    for name, entry in data.items():
        where = f'presets.json "{name}"'
        if not name.lower().endswith(".ini"):
            report.error(f"{where}: key must be the .ini file name")
        if not isinstance(entry, dict):
            report.error(f"{where}: value must be an object")
            continue

        if name.lower() not in presets:
            report.error(f"{where}: no such file in Presets/")
        elif presets[name.lower()] != name:
            report.error(f'{where}: case differs from the file "{presets[name.lower()]}"')

        unknown = set(entry) - PRESET_FIELDS
        if unknown:
            report.warn(f"{where}: unknown field(s) {sorted(unknown)} (typo?)")

        for field in PRESET_FIELDS & set(entry):
            if entry[field] is not None and not isinstance(entry[field], str):
                report.error(f'{where}: "{field}" must be text')

        if not str(entry.get("description") or "").strip():
            report.warn(f"{where}: no description")

        link = str(entry.get("videoLink") or "").strip()
        if link and not link.startswith(("http://", "https://")):
            report.error(f'{where}: videoLink must start with http:// or https:// (got "{link}")')

        preview = str(entry.get("preset-preview") or "").strip()
        if not preview:
            report.warn(f"{where}: no preset-preview")
        elif preview.lower() not in previews:
            report.error(f'{where}: preview "{preview}" not found in media/preview-preset/')
        elif previews[preview.lower()] != preview:
            report.error(f'{where}: preview case differs from the file "{previews[preview.lower()]}"')

    described = {k.lower() for k in data}
    for lower, real in sorted(presets.items()):
        if lower.endswith(".ini") and lower not in described:
            report.warn(f'Presets/{real}: no entry in presets.json (no description or preview)')

    print(f"presets.json: {len(data)} presets")


# ---------------------------------------------------------------------------------------------------
# translations/*.json
# ---------------------------------------------------------------------------------------------------

def check_translations(report):
    if not os.path.isdir(TRANSLATIONS_DIR):
        report.error("translations/ folder not found")
        return

    reference = load_json(os.path.join(TRANSLATIONS_DIR, "en.json"), "translations/en.json", report)
    if reference is None:
        report.error("translations/en.json is the reference file: it must exist and be valid")
        return
    keys = {k: v for k, v in reference.items() if not k.startswith("_")}

    rows = []
    for file in sorted(os.listdir(TRANSLATIONS_DIR)):
        if not file.endswith(".json"):
            continue
        label = f"translations/{file}"
        code = file[:-5]
        data = load_json(os.path.join(TRANSLATIONS_DIR, file), label, report)
        if data is None:
            rows.append((file, "?", "invalid"))
            continue

        if not LANG_CODE.match(code):
            report.error(f'{label}: file name must be a language code like "pl.json" or "pt-br.json"')
        if not str(data.get("_language", "")).strip():
            report.error(f'{label}: "_language" (the language name, e.g. "Polski") is missing')

        missing = [k for k in keys if k not in data]
        if missing:
            report.warn(f"{label}: {len(missing)} missing key(s), English will be shown: "
                        + ", ".join(missing[:8]) + (" …" if len(missing) > 8 else ""))
        for k in data:
            if not k.startswith("_") and k not in keys:
                report.warn(f'{label}: unknown key "{k}" (typo, or removed from en.json?)')

        for key, text in data.items():
            if key.startswith("_") or key not in keys:
                continue
            if not isinstance(text, str):
                report.error(f'{label}: "{key}" must be text')
                continue
            if not text.strip():
                report.warn(f'{label}: "{key}" is empty, English will be shown')
                continue
            expected = sorted(PLACEHOLDER.findall(keys[key]))
            found = sorted(PLACEHOLDER.findall(text))
            if expected != found:
                report.error(f'{label}: "{key}" must contain {" ".join(expected) or "no placeholders"}, '
                             f'found {" ".join(found) or "none"}')

        done = len(keys) - len(missing)
        rows.append((file, str(data.get("_language", "?")), f"{done}/{len(keys)} ({100 * done // max(len(keys), 1)}%)"))

    print("translations:")
    for file, language, status in rows:
        print(f"  {file:<12} {language:<14} {status}")


# ---------------------------------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(description="Validate presets.json and translations.")
    parser.add_argument("--presets", action="store_true", help="only check media/presets.json")
    parser.add_argument("--translations", action="store_true", help="only check translations/*.json")
    args = parser.parse_args()
    run_all = not (args.presets or args.translations)

    report = Report()
    if run_all or args.presets:
        check_presets(report)
    if run_all or args.translations:
        check_translations(report)

    if report.warnings or report.errors:
        print()
    for w in report.warnings:
        print(f"warning: {w}")
    for e in report.errors:
        print(f"ERROR: {e}")

    print(f"\n{'OK' if not report.errors else 'FAILED'}: {len(report.errors)} error(s), {len(report.warnings)} warning(s)")
    return 1 if report.errors else 0


if __name__ == "__main__":
    sys.exit(main())
