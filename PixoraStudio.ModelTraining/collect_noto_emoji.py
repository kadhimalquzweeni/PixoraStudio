"""Collect the pinned Noto Emoji 128px PNG artwork used for sprite training."""
from __future__ import annotations

import argparse
import csv
import hashlib
import io
import json
import re
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
from urllib.request import Request, urlopen

from PIL import Image

REPOSITORY = "googlefonts/noto-emoji"
COMMIT = "99701e07d3d1fe1bdbcdb14964547f0fd592966f"
SOURCE_DIRECTORY = "2D/png/128"
USER_AGENT = "PixoraStudio-NotoEmoji-Dataset/1.0"
FILENAME_PATTERN = re.compile(r"^emoji_u[0-9a-f]+(?:_[0-9a-f]+)*\.png$", re.IGNORECASE)


def fetch(url: str, attempts: int = 5) -> bytes:
    for attempt in range(attempts):
        try:
            request = Request(url, headers={"User-Agent": USER_AGENT})
            with urlopen(request, timeout=45) as response:
                return response.read()
        except Exception:
            if attempt + 1 == attempts:
                raise
            time.sleep(min(2**attempt, 12))
    raise RuntimeError(f"Could not fetch {url}")


def has_flag_codepoint(filename: str) -> bool:
    parts = Path(filename).stem.removeprefix("emoji_u").split("_")
    try:
        codepoints = [int(part, 16) for part in parts]
    except ValueError:
        return True
    return any(0x1F1E6 <= value <= 0x1F1FF or 0xE0020 <= value <= 0xE007F
               for value in codepoints)


def git_blob_sha(data: bytes) -> str:
    header = f"blob {len(data)}\0".encode("ascii")
    return hashlib.sha1(header + data).hexdigest()


def valid_png(data: bytes) -> bool:
    try:
        with Image.open(io.BytesIO(data)) as image:
            if image.size != (128, 128):
                return False
            rgba = image.convert("RGBA")
            low, high = rgba.getchannel("A").getextrema()
            return low < 255 and high > 0
    except (OSError, ValueError):
        return False


def download_one(entry: dict[str, str], output: Path) -> dict[str, str] | None:
    filename = Path(entry["path"]).name
    target = output / filename
    data = target.read_bytes() if target.is_file() else b""
    if not data or git_blob_sha(data) != entry["sha"]:
        url = f"https://raw.githubusercontent.com/{REPOSITORY}/{COMMIT}/{entry['path']}"
        data = fetch(url)
    if git_blob_sha(data) != entry["sha"]:
        raise RuntimeError(f"Git blob SHA mismatch for {filename}")
    if not valid_png(data):
        return None
    if not target.is_file() or target.read_bytes() != data:
        target.write_bytes(data)
    return {
        "filename": filename,
        "source_path": entry["path"],
        "source_url": f"https://github.com/{REPOSITORY}/blob/{COMMIT}/{entry['path']}",
        "source_commit": COMMIT,
        "source_git_blob_sha": entry["sha"],
        "sha256": hashlib.sha256(data).hexdigest(),
        "width": "128",
        "height": "128",
        "license": "Apache-2.0",
        "creator": "Google LLC and Noto Emoji contributors",
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, default=Path("data/hr/noto_emoji_128"))
    parser.add_argument("--workers", type=int, default=8)
    args = parser.parse_args()
    if args.workers < 1 or args.workers > 16:
        parser.error("workers must be between 1 and 16")

    output = args.output_dir.resolve()
    output.mkdir(parents=True, exist_ok=True)
    tree_url = f"https://api.github.com/repos/{REPOSITORY}/git/trees/{COMMIT}?recursive=1"
    tree = json.loads(fetch(tree_url))
    if tree.get("truncated"):
        raise SystemExit("The upstream repository tree was truncated; refusing an incomplete collection.")
    entries = [
        item for item in tree["tree"]
        if item.get("type") == "blob"
        and item.get("path", "").startswith(SOURCE_DIRECTORY + "/")
        and item.get("path", "").lower().endswith(".png")
        and FILENAME_PATTERN.fullmatch(Path(item["path"]).name)
        and not has_flag_codepoint(Path(item["path"]).name)
    ]
    if not entries:
        raise SystemExit("No eligible Noto Emoji PNGs were found at the pinned source revision.")

    results: list[dict[str, str]] = []
    errors: list[str] = []
    with ThreadPoolExecutor(max_workers=args.workers) as executor:
        futures = {executor.submit(download_one, entry, output): entry for entry in entries}
        for completed, future in enumerate(as_completed(futures), start=1):
            try:
                result = future.result()
                if result:
                    results.append(result)
            except Exception as error:
                errors.append(f"{futures[future]['path']}: {error}")
            if completed % 200 == 0 or completed == len(futures):
                print(f"Processed {completed}/{len(futures)} source files; kept {len(results)} transparent 128px images.", flush=True)

    if errors:
        raise SystemExit("Collection incomplete; rerun to resume. First errors:\n" + "\n".join(errors[:10]))
    results.sort(key=lambda row: row["filename"])
    ledger = output / "attribution.csv"
    with ledger.open("w", newline="", encoding="utf-8-sig") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(results[0]))
        writer.writeheader()
        writer.writerows(results)

    base_url = f"https://raw.githubusercontent.com/{REPOSITORY}/{COMMIT}"
    for local_name, upstream_name in (("UPSTREAM_README.md", "README.md"),
                                      ("UPSTREAM_LICENSE.txt", "LICENSE")):
        (output / local_name).write_bytes(fetch(f"{base_url}/{upstream_name}"))
    (output / "DATASET_INFO.txt").write_text(
        "Noto Emoji sprite training images\n"
        f"Upstream: https://github.com/{REPOSITORY}\n"
        f"Pinned commit: {COMMIT}\n"
        f"Source folder: {SOURCE_DIRECTORY}\n"
        "The upstream README states that most image resources are Apache-2.0; region-flag artwork is identified separately.\n"
        "This collection excludes regional-indicator and tag-sequence flags, and records each asset URL and source blob SHA in attribution.csv.\n"
        "Retain the upstream README, license, and attribution.csv with these files.\n",
        encoding="utf-8",
    )
    print(f"Finished: {len(results)} images saved to {output}")
    print(f"Attribution ledger: {ledger}")


if __name__ == "__main__":
    main()
