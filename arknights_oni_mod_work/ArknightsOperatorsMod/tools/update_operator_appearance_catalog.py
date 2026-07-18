#!/usr/bin/env python3
"""Build the small in-game operator/skin/model catalog from PRTS metadata."""

from __future__ import annotations

import argparse
import concurrent.futures
import json
import pathlib
import re
import time
import urllib.parse
import urllib.request


MAX_RESPONSE_BYTES = 1024 * 1024
MAX_TOTAL_BYTES = 16 * 1024 * 1024
META_URL = "https://torappu.prts.wiki/assets/char_spine/{character_id}/meta.json"
PRTS_API_URL = "https://prts.wiki/api.php"
ALIAS_BATCH_SIZE = 30
THUMBNAIL_BATCH_SIZE = 30
THUMBNAIL_WIDTH = 96


def read_operators(path: pathlib.Path) -> list[dict[str, str]]:
    source = path.read_text(encoding="utf-8-sig")
    match = re.search(r"var\s+char_json\s*=\s*(\[.*\])\s*;?\s*$", source, re.DOTALL)
    if match is None:
        raise ValueError(f"Cannot find char_json in {path}")
    operators = json.loads(match.group(1))
    if not isinstance(operators, list) or not operators:
        raise ValueError("Operator list is empty")
    return operators


def fetch_aliases(
    operators: list[dict[str, str]], retries: int
) -> tuple[dict[str, list[str]], dict[str, str], dict[str, str]]:
    names = sorted({str(operator["name"]) for operator in operators})
    aliases_by_name: dict[str, list[str]] = {name: [] for name in names}
    english_by_name: dict[str, str] = {}
    japanese_by_name: dict[str, str] = {}
    batches = [
        names[start : start + ALIAS_BATCH_SIZE]
        for start in range(0, len(names), ALIAS_BATCH_SIZE)
    ]
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as executor:
        for batch_aliases, batch_english, batch_japanese in executor.map(
            lambda batch: fetch_alias_batch(batch, retries), batches
        ):
            for name, aliases in batch_aliases.items():
                aliases_by_name[name].extend(aliases)
            english_by_name.update(batch_english)
            japanese_by_name.update(batch_japanese)

    return aliases_by_name, english_by_name, japanese_by_name


def fetch_alias_batch(
    batch: list[str], retries: int
) -> tuple[dict[str, list[str]], dict[str, str], dict[str, str]]:
    result: dict[str, list[str]] = {name: [] for name in batch}
    english_names: dict[str, str] = {}
    japanese_names: dict[str, str] = {}
    params: dict[str, str] = {
        "action": "query",
        "format": "json",
        "formatversion": "2",
        "prop": "redirects|revisions",
        "rdlimit": "max",
        "rvprop": "content",
        "rvslots": "main",
        "rvsection": "0",
        "titles": "|".join(batch),
    }
    while True:
        url = PRTS_API_URL + "?" + urllib.parse.urlencode(params)
        request = urllib.request.Request(
            url,
            headers={"User-Agent": "ArknightsONIMod-CatalogBuilder/1.0"},
        )
        body: bytes | None = None
        last_error: Exception | None = None
        for attempt in range(max(1, retries + 1)):
            try:
                with urllib.request.urlopen(request, timeout=30) as response:
                    content_length = response.headers.get("Content-Length")
                    if content_length is not None and int(content_length) > MAX_RESPONSE_BYTES:
                        raise ValueError("PRTS alias response exceeds 1 MiB")
                    body = response.read(MAX_RESPONSE_BYTES + 1)
                break
            except Exception as error:
                last_error = error
                if attempt < retries:
                    time.sleep(0.5 * (attempt + 1))
        if body is None:
            raise last_error or RuntimeError("Failed to fetch PRTS aliases")
        if len(body) > MAX_RESPONSE_BYTES:
            raise ValueError("PRTS alias response exceeds 1 MiB")

        payload = json.loads(body.decode("utf-8-sig"))
        for page in payload.get("query", {}).get("pages", []):
            title = str(page.get("title", ""))
            if title not in result:
                continue
            revisions = page.get("revisions", [])
            if revisions:
                content = str(
                    revisions[0].get("slots", {}).get("main", {}).get("content", "")
                )
                page_name = re.search(
                    r"\{\{干员页面名\|[^|{}]*\|([^|{}]*)\|([^|{}]*)", content
                )
                if page_name is not None:
                    english_name = page_name.group(1).strip()
                    japanese_name = page_name.group(2).strip()
                    if english_name:
                        english_names[title] = english_name
                    if japanese_name:
                        japanese_names[title] = japanese_name
            for redirect in page.get("redirects", []):
                alias = str(redirect.get("title", "")).strip()
                if alias and len(alias) <= 80 and alias not in result[title]:
                    result[title].append(alias)

        continuation = payload.get("continue")
        if not isinstance(continuation, dict):
            break
        for key, value in continuation.items():
            params[str(key)] = str(value)
    return result, english_names, japanese_names


def choose_english_name(name: str, aliases: list[str]) -> str | None:
    candidates = [name, *aliases]
    for candidate in candidates:
        if re.search(r"[A-Za-z]", candidate):
            return candidate
    return None


def fetch_thumbnail_urls(
    operators: list[dict[str, str]], retries: int
) -> dict[str, str]:
    names = sorted({str(operator["name"]) for operator in operators})
    batches = [
        names[start : start + THUMBNAIL_BATCH_SIZE]
        for start in range(0, len(names), THUMBNAIL_BATCH_SIZE)
    ]
    thumbnails: dict[str, str] = {}
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
        for batch_result in executor.map(
            lambda batch: fetch_thumbnail_batch(batch, retries), batches
        ):
            thumbnails.update(batch_result)
    return thumbnails


def fetch_thumbnail_batch(batch: list[str], retries: int) -> dict[str, str]:
    title_to_name = {f"文件:头像 {name}.png": name for name in batch}
    params = {
        "action": "query",
        "format": "json",
        "formatversion": "2",
        "prop": "imageinfo",
        "iiprop": "url|size|mime",
        "iiurlwidth": str(THUMBNAIL_WIDTH),
        "titles": "|".join(title_to_name),
    }
    url = PRTS_API_URL + "?" + urllib.parse.urlencode(params)
    request = urllib.request.Request(
        url,
        headers={"User-Agent": "ArknightsONIMod-CatalogBuilder/1.0"},
    )
    body: bytes | None = None
    last_error: Exception | None = None
    for attempt in range(max(1, retries + 1)):
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                content_length = response.headers.get("Content-Length")
                if content_length is not None and int(content_length) > MAX_RESPONSE_BYTES:
                    raise ValueError("PRTS thumbnail response exceeds 1 MiB")
                body = response.read(MAX_RESPONSE_BYTES + 1)
            break
        except Exception as error:
            last_error = error
            if attempt < retries:
                time.sleep(0.5 * (attempt + 1))
    if body is None:
        raise last_error or RuntimeError("Failed to fetch PRTS thumbnail metadata")
    if len(body) > MAX_RESPONSE_BYTES:
        raise ValueError("PRTS thumbnail response exceeds 1 MiB")

    result: dict[str, str] = {}
    payload = json.loads(body.decode("utf-8-sig"))
    for page in payload.get("query", {}).get("pages", []):
        name = title_to_name.get(str(page.get("title", "")))
        image_info = page.get("imageinfo", [])
        if name is None or not image_info:
            continue
        thumbnail_url = str(image_info[0].get("thumburl", "")).strip()
        parsed = urllib.parse.urlparse(thumbnail_url)
        if parsed.scheme == "https" and parsed.hostname == "media.prts.wiki":
            result[name] = thumbnail_url
    return result


def fetch_meta(
    operator: dict[str, str], aliases: list[str], english_name: str | None,
    japanese_name: str | None, thumbnail_url: str | None,
    cache_dir: pathlib.Path, retries: int
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

    name = str(operator["name"])
    record: dict[str, object] = {
        "id": character_id,
        "name": name,
        "skins": skin_records,
    }
    if thumbnail_url is not None:
        record["thumbnail_url"] = thumbnail_url
    english_name = english_name or choose_english_name(name, aliases)
    if english_name is not None and english_name != name:
        record["english_name"] = english_name
    if japanese_name is not None and japanese_name != name:
        record["japanese_name"] = japanese_name
    useful_aliases = [
        alias for alias in aliases if alias not in {name, english_name, japanese_name}
    ]
    if useful_aliases:
        record["aliases"] = useful_aliases
    return record, fetched_bytes


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
        "--reuse-existing",
        action="store_true",
        help="Enrich the existing catalog with PRTS aliases without refetching model metadata",
    )
    parser.add_argument(
        "--thumbnails-only",
        action="store_true",
        help="Refresh only 96px thumbnail URLs in the existing catalog",
    )
    parser.add_argument(
        "--cache-dir",
        type=pathlib.Path,
        default=pathlib.Path(".cache/operator-meta"),
    )
    args = parser.parse_args()

    if args.thumbnails_only:
        catalog = json.loads(args.output.read_text(encoding="utf-8-sig"))
        records = catalog.get("operators")
        if not isinstance(records, list) or not records:
            raise ValueError("Existing catalog is empty")
        operators = [
            {"id": str(record["id"]), "name": str(record["name"])}
            for record in records
        ]
        thumbnails_by_name = fetch_thumbnail_urls(operators, args.retries)
        for record in records:
            thumbnail_url = thumbnails_by_name.get(str(record["name"]))
            if thumbnail_url is not None:
                record["thumbnail_url"] = thumbnail_url
        catalog["thumbnail_source"] = (
            PRTS_API_URL
            + "?action=query&prop=imageinfo&iiprop=url%7Csize%7Cmime&iiurlwidth=96"
        )
        args.output.write_text(
            json.dumps(catalog, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
        print(
            f"Enriched {len(thumbnails_by_name)} thumbnail URLs in {args.output} "
            f"({args.output.stat().st_size} bytes)"
        )
        return
    operators = read_operators(args.operators)
    aliases_by_name, english_by_name, japanese_by_name = fetch_aliases(
        operators, args.retries
    )
    thumbnails_by_name = fetch_thumbnail_urls(operators, args.retries)
    if args.reuse_existing:
        catalog = json.loads(args.output.read_text(encoding="utf-8-sig"))
        records = catalog.get("operators")
        if not isinstance(records, list) or len(records) != len(operators):
            raise ValueError("Existing catalog does not match the PRTS operator snapshot")
        for record in records:
            name = str(record["name"])
            aliases = aliases_by_name.get(name, [])
            english_name = english_by_name.get(name) or choose_english_name(name, aliases)
            japanese_name = japanese_by_name.get(name)
            thumbnail_url = thumbnails_by_name.get(name)
            record.pop("english_name", None)
            record.pop("japanese_name", None)
            record.pop("aliases", None)
            record.pop("thumbnail_url", None)
            if english_name is not None and english_name != name:
                record["english_name"] = english_name
            if japanese_name is not None and japanese_name != name:
                record["japanese_name"] = japanese_name
            useful_aliases = [
                alias
                for alias in aliases
                if alias not in {name, english_name, japanese_name}
            ]
            if useful_aliases:
                record["aliases"] = useful_aliases
            if thumbnail_url is not None:
                record["thumbnail_url"] = thumbnail_url
        catalog["alias_source"] = (
            PRTS_API_URL + "?action=query&prop=redirects%7Crevisions&rvsection=0"
        )
        catalog["thumbnail_source"] = (
            PRTS_API_URL
            + "?action=query&prop=imageinfo&iiprop=url%7Csize%7Cmime&iiurlwidth=96"
        )
        args.output.write_text(
            json.dumps(catalog, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
        print(
            f"Enriched {len(records)} operators in {args.output} "
            f"({args.output.stat().st_size} bytes)"
        )
        return
    records: list[dict[str, object]] = []
    total_bytes = 0
    failures: list[str] = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=max(1, args.workers)) as executor:
        futures = {
            executor.submit(
                fetch_meta,
                operator,
                aliases_by_name.get(str(operator["name"]), []),
                english_by_name.get(str(operator["name"])),
                japanese_by_name.get(str(operator["name"])),
                thumbnails_by_name.get(str(operator["name"])),
                args.cache_dir,
                args.retries,
            ): operator
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
        "alias_source": (
            PRTS_API_URL + "?action=query&prop=redirects%7Crevisions&rvsection=0"
        ),
        "meta_source_pattern": META_URL,
        "thumbnail_source": (
            PRTS_API_URL
            + "?action=query&prop=imageinfo&iiprop=url%7Csize%7Cmime&iiurlwidth=96"
        ),
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
