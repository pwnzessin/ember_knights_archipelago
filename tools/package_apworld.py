"""Package the Ember Knights world as an .apworld using Archipelago's own container format.

This mirrors the "Build APWorlds" launcher component: the source archipelago.json is merged
with APWorldContainer.get_manifest() (adds version/compatible_version/game) and stored at
<world>/archipelago.json inside the archive.
"""

import json
import sys
import zipfile
from pathlib import Path

ARCHIPELAGO_ROOT = Path(sys.argv[1]).resolve()
WORLD_DIRECTORY = Path(sys.argv[2]).resolve()
OUTPUT_PATH = Path(sys.argv[3]).resolve()

sys.path.insert(0, str(ARCHIPELAGO_ROOT))

from worlds.Files import APWorldContainer  # noqa: E402

IGNORED_DIRECTORIES = {"__pycache__"}
IGNORED_SUFFIXES = {".pyc"}


def main() -> int:
    file_name = WORLD_DIRECTORY.name
    manifest_source = WORLD_DIRECTORY / "archipelago.json"
    manifest = json.loads(manifest_source.read_text(encoding="utf-8"))
    for forbidden in ("version", "compatible_version"):
        if forbidden in manifest:
            raise SystemExit(f"archipelago.json must not define '{forbidden}'; the container adds it.")

    container = APWorldContainer(str(OUTPUT_PATH))
    container.game = manifest["game"]
    manifest.update(container.get_manifest())
    container.manifest_path = f"{file_name}/archipelago.json"

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    if OUTPUT_PATH.exists():
        OUTPUT_PATH.unlink()
    with zipfile.ZipFile(OUTPUT_PATH, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(WORLD_DIRECTORY.rglob("*")):
            if not path.is_file() or path == manifest_source:
                continue
            relative = path.relative_to(WORLD_DIRECTORY)
            if IGNORED_DIRECTORIES.intersection(relative.parts) or path.suffix in IGNORED_SUFFIXES:
                continue
            archive.write(path, Path(file_name, relative).as_posix())
        archive.writestr(container.manifest_path, json.dumps(manifest))

    # Read it back with the real loader to prove the archive is valid.
    reader = APWorldContainer(str(OUTPUT_PATH))
    reader.read()
    print(f"Packaged {OUTPUT_PATH} (game={reader.game}, manifest={manifest})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
