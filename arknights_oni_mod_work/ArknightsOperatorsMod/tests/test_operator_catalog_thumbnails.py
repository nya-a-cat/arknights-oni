#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import json
import pathlib
import sys
import tempfile
import threading
import unittest
import urllib.parse
from unittest import mock


ROOT = pathlib.Path(__file__).resolve().parents[1]
MODULE_PATH = ROOT / "tools" / "update_operator_appearance_catalog.py"
SPEC = importlib.util.spec_from_file_location("operator_catalog_builder", MODULE_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Cannot load {MODULE_PATH}")
catalog_builder = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = catalog_builder
SPEC.loader.exec_module(catalog_builder)


class FakeResponse:
    def __init__(self, payload: dict[str, object]) -> None:
        self.body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.headers = {"Content-Length": str(len(self.body))}

    def __enter__(self) -> "FakeResponse":
        return self

    def __exit__(self, *args: object) -> None:
        return None

    def read(self, limit: int) -> bytes:
        return self.body[:limit]


class ThumbnailCatalogTests(unittest.TestCase):
    def test_batch_queries_store_only_media_thumbnail_urls(self) -> None:
        requested_urls: list[str] = []
        request_gate = threading.Lock()

        def urlopen(request: object, timeout: int) -> FakeResponse:
            del timeout
            url = request.full_url  # type: ignore[attr-defined]
            with request_gate:
                requested_urls.append(url)
            parsed = urllib.parse.urlparse(url)
            self.assertEqual(parsed.netloc, "prts.wiki")
            query = urllib.parse.parse_qs(parsed.query)
            self.assertEqual(query["prop"], ["imageinfo"])
            self.assertEqual(query["iiurlwidth"], ["96"])
            titles = query["titles"][0].split("|")
            self.assertLessEqual(len(titles), 30)
            pages = []
            for title in titles:
                name = title[len("文件:头像 ") : -len(".png")]
                pages.append(
                    {
                        "title": title,
                        "imageinfo": [
                            {
                                "thumburl": (
                                    "https://media.prts.wiki/thumb/"
                                    + urllib.parse.quote(name)
                                    + ".png/96px-portrait.png"
                                ),
                                "mime": "image/png",
                            }
                        ],
                    }
                )
            return FakeResponse({"query": {"pages": pages}})

        operators = [
            {"id": f"char_{index}", "name": f"测试{index}"}
            for index in range(31)
        ]
        with mock.patch.object(catalog_builder.urllib.request, "urlopen", urlopen):
            thumbnails = catalog_builder.fetch_thumbnail_urls(operators, retries=0)

        self.assertEqual(len(requested_urls), 2)
        self.assertEqual(len(thumbnails), 31)
        self.assertTrue(
            all(url.startswith("https://media.prts.wiki/") for url in thumbnails.values())
        )

    def test_meta_record_keeps_url_without_fetching_the_image(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            cache = pathlib.Path(directory)
            (cache / "char_test.json").write_text(
                json.dumps({"skin": {"默认": {"基建": {"file": "test"}}}}),
                encoding="utf-8",
            )
            thumbnail_url = "https://media.prts.wiki/thumb/test.png/96px-test.png"
            record, fetched_bytes = catalog_builder.fetch_meta(
                {"id": "char_test", "name": "测试"},
                [],
                "Test",
                None,
                thumbnail_url,
                cache,
                retries=0,
            )
        self.assertEqual(fetched_bytes, 0)
        self.assertEqual(record["thumbnail_url"], thumbnail_url)


if __name__ == "__main__":
    unittest.main()
