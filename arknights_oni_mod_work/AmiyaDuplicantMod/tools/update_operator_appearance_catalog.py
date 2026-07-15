#!/usr/bin/env python3
"""Build the small in-game operator/skin/model catalog from PRTS metadata."""

from __future__ import annotations

import argparse
import concurrent.futures
import json
import pathlib
import re
import time
import urllib.request


MAX_RESPONSE_BYTES = 1024 * 1024
MAX_TOTAL_BYTES = 16 * 1024 * 1024
META_URL = "https://torappu.prts.wiki/assets/char_spine/{character_id}/meta.json"


def read_operators(path: pathlib.Path) -> list[dict[str, str]]:
    source = path.read_text(encoding="utf-8-sig")
    match = re.search(r"var\s+char_json\s*=\s*(\[.*\])\s*;?\s*$", source, re.DOTALL)
    if match is None:
        raise ValueError(f"Cannot find char_json in {path}")
    operators = json.loads(match.group(1))
    if not isinstance(operators, list) or not operators:
        raise ValueError("Operator list is empty")
    return operators


def fetch_meta(
    operator: dict[str, str], cache_dir: pathlib.Path, retries: int
) -> tuple[dict[str, object], int]:
    character_id = operator["id"]
    cache_path = cache_dir / f"{character_id}.json"
    body = cache_path.read_bytes() if cache_path.exists() else None
    fetched_bytes = 0
    if body is None:
        request = urllib.request.Request(
            META_URL.format(character_id=character_id),
            headers={"User-Agent": "ArknightsONIMod-CatalogBuilder/1.0"},
        )
        last_error: Exception | None = None
        for attempt in range(max(1, retries + 1)):
            try:
                with urllib.request.urlopen(request, timeout=20) as response:
                    content_length = response.headers.get("Content-Length")
                    if content_length is not None and int(content_length) > MAX_RESPONSE_BYTES:
                        raise ValueError(f"{character_id} metadata exceeds 1 MiB")
                    body = response.read(MAX_RESPONSE_BYTES + 1)
                break
            except Exception as error:
                last_error = error
                if attempt < retries:
                    time.sleep(0.5 * (attempt + 1))
        if body is None:
            raise last_error or RuntimeError(f"Failed to fetch {character_id}")
        fetched_bytes = len(body)
        cache_dir.mkdir(parents=True, exist_ok=True)
        cache_path.write_bytes(body)
    if len(body) > MAX_RESPONSE_BYTES:
        raise ValueError(f"{character_id} metadata exceeds 1 MiB")

    meta = json.loads(body.decode("utf-8-sig"))
    skins = meta.get("skin")
    if not isinstance(skins, dict) or not skins:
        raise ValueError(f"{character_id} has no skin metadata")

    skin_records: list[dict[str, object]] = []
    for skin_name, models in skins.items():
        if not isinstance(models, dict) or not models:
            continue
        model_names = sorted(str(name) for name in models)
        skin_records.append({"name": str(skin_name), "models": model_names})
    skin_records.sort(key=lambda item: (item["name"] != "默认", str(item["name"])))
    if not skin_records:
        raise ValueError(f"{character_id} has no usable models")

    return (
        {
            "id": character_id,
            "name": str(operator["name"]),
            "skins": skin_records,
        },
        fetched_bytes,
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--operators",
        type=pathlib.Path,
        default=pathlib.Path("preview/prts_operator_catalog_20260604.js"),
    )
    parser.add_argument(
        "--output",
        type=pathlib.Path,
        default=pathlib.Path("assets/catalog/operator_appearances_20260604.json"),
    )
    parser.add_argument("--workers", type=int, default=4)
    parser.add_argument("--retries", type=int, default=2)
    parser.add_argument(
        "--cache-dir",
        type=pathlib.Path,
        default=pathlib.Path(".cache/operator-meta"),
    )
    args = parser.parse_args()

    operators = read_operators(args.operators)
    records: list[dict[str, object]] = []
    total_bytes = 0
    failures: list[str] = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=max(1, args.workers)) as executor:
        futures = {
            executor.submit(fetch_meta, operator, args.cache_dir, args.retries): operator
            for operator in operators
        }
        for future in concurrent.futures.as_completed(futures):
            operator = futures[future]
            try:
                record, response_bytes = future.result()
                records.append(record)
                total_bytes += response_bytes
                if total_bytes > MAX_TOTAL_BYTES:
                    raise ValueError("Combined metadata exceeds 16 MiB")
            except Exception as error:  # report every failed character together
                failures.append(f"{operator['id']}: {error}")

    if failures:
        raise RuntimeError("Failed metadata requests:\n" + "\n".join(sorted(failures)))
    if len(records) != len(operators):
        raise RuntimeError(f"Expected {len(operators)} records, got {len(records)}")

    records.sort(key=lambda item: str(item["id"]))
    catalog = {
        "schema_version": 1,
        "snapshot_date": "2026-06-04",
        "character_source": "https://static.prts.wiki/charinfo/charId20260604.js",
        "meta_source_pattern": META_URL,
        "operators": records,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(catalog, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(
        f"Wrote {len(records)} operators to {args.output} "
        f"({args.output.stat().st_size} bytes; fetched {total_bytes} bytes)"
    )


if __name__ == "__main__":
    main()
