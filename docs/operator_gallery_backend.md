# Operator gallery thumbnail backend

This document records the data and cache contract used by the in-game operator gallery.

## Catalog contract

The operator catalog remains at schema version `1`. Each operator may include an optional
`thumbnail_url` field. Older catalogs without that field continue to load and the gallery must
render a name placeholder for those entries.

`tools/update_operator_appearance_catalog.py` queries MediaWiki `imageinfo` in batches of 30 with
`iiurlwidth=96`. It stores the returned `https://media.prts.wiki/...` thumbnail URL only. The
generator never downloads thumbnail image bytes. `--thumbnails-only` refreshes URLs in an existing
catalog without requesting aliases or Spine metadata.

## Runtime cache contract

`OperatorThumbnailLoader` creates one request per appearance:

- key: `thumbnail:<char_id>:96`
- relative path: `thumbnails/96/<char_id>.img`
- resource version: the complete thumbnail URL
- per-file limit: `256 KiB`
- simultaneous visible-page loads: at most `2`

The loader uses `PrtsResourceService`, so thumbnails participate in the existing LRU capacity and
permanent-retention policy. A returned `OperatorThumbnailAsset` holds a resource lease until it is
disposed. Disposing the loader cancels pending UI waits and releases every asset still owned by the
window. A shared download that already started may finish in the background and enter the cache.

Before Unity image decoding, `OperatorThumbnailFile.Inspect` accepts PNG or JPEG headers and rejects
files over `256 KiB` or decoded dimensions above `256 x 256`.

## UI integration points

The gallery window should create one `OperatorThumbnailLoader` and keep the returned assets for the
current page only. Page changes dispose those assets before requesting the next 20 visible cards.
Window close disposes the loader. Texture creation and `Texture2D.LoadImage` stay on Unity's main
thread; network and file waiting may remain asynchronous. A missing URL, cancellation, validation
failure, or offline miss maps to the existing name placeholder card.

The Mods options page must not create this loader. Thumbnail activity begins only after the user
opens the operator gallery.

## Verification

- `tests/test_operator_catalog_thumbnails.py` mocks MediaWiki and checks 30-title batching, 96px URL
  capture, and URL-only catalog records.
- `tests/run_operator_thumbnail_loader_tests.sh` uses an offline HTTP handler to check the approved
  media host, request identity, cache reuse, 256 KiB limits, PNG/JPEG dimensions, leases, and
  close-time cancellation.
