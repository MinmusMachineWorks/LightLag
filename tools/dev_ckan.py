#!/usr/bin/env python3
"""Writes ckan/LightLag-dev.ckan: a CKAN metapackage for a local dev install.

The metapackage carries LightLag's relations from ckan/metadata.json (depends,
recommends, suggests, conflicts) but installs no files. Install it once in CKAN
(GUI: File > Install from .ckan...), and CKAN installs and keeps LightLag's
dependencies and enforces its conflicts, while `dotnet build` copies the dev
build of LightLag itself into GameData.

Usage:
    python3 tools/dev_ckan.py                    # write ckan/LightLag-dev.ckan
    python3 tools/dev_ckan.py --gamedir <KSP>    # also install it with the ckan CLI
                                                 # (close the CKAN GUI first)

The version is the time of the last commit that changed ckan/metadata.json, or
the current time if it has uncommitted changes, so it only goes up when the
relations do.
"""
import argparse
import datetime
import json
import pathlib
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SOURCE = ROOT / "ckan" / "metadata.json"
OUTPUT = ROOT / "ckan" / "LightLag-dev.ckan"
RELATIONS = ("depends", "recommends", "suggests", "conflicts")


def git(*args):
    return subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True).stdout.strip()


def version():
    dirty = git("status", "--porcelain", "--", str(SOURCE.relative_to(ROOT)))
    stamp = None if dirty else git("log", "-1", "--format=%ct", "--", str(SOURCE.relative_to(ROOT)))
    when = (datetime.datetime.fromtimestamp(int(stamp), datetime.timezone.utc) if stamp
            else datetime.datetime.now(datetime.timezone.utc))
    return "0.0.0." + when.strftime("%Y%m%d%H%M%S")


def build():
    meta = json.loads(SOURCE.read_text())
    ident = meta["identifier"]
    ckan = {
        "spec_version": "v1.6",
        "identifier": ident + "-dev",
        "name": meta["name"] + " (dev build dependencies)",
        "abstract": f"Installs {ident}'s dependencies and enforces its conflicts for a local dev build. "
                    f"Installs no files: the {ident} build copies itself into GameData.",
        "kind": "metapackage",
        "version": version(),
        "author": meta["author"],
        "license": meta["license"],
        "release_status": "development",
        "resources": {"repository": meta["resources"]["repository"]},
    }
    for key in ("ksp_version", "ksp_version_min", "ksp_version_max"):
        if key in meta:
            ckan[key] = meta[key]
    for key in RELATIONS:
        if meta.get(key):
            ckan[key] = meta[key]
    return ckan


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--gamedir", help="KSP folder to install the metapackage into with the ckan CLI")
    args = parser.parse_args()

    ckan = build()
    OUTPUT.write_text(json.dumps(ckan, indent=4) + "\n")
    print(f"wrote {OUTPUT}: {ckan['identifier']} {ckan['version']}")
    for key in RELATIONS:
        if key in ckan:
            print(f"  {key}: {', '.join(r['name'] for r in ckan[key])}")

    if args.gamedir:
        # --no-recommends: recommended mods are a choice, so the CLI doesn't pull them in.
        cmd = ["ckan", "install", "--headless", "--no-recommends", "--gamedir", args.gamedir, "--ckanfiles", str(OUTPUT)]
        print("running: " + " ".join(cmd))
        sys.exit(subprocess.run(cmd).returncode)


if __name__ == "__main__":
    main()
