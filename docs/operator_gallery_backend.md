# Operator gallery thumbnail backend

This document records the data and cache contract used by the in-game operator gallery.

## Catalog contract

The operator catalog remains at schema version `1`. Each operator may include an optional
`thumbnail_url` field. Older catalogs without that field continue to load and the gallery must
render a name placeholder for those entries.

`tools/update_operator_appearance_catalog.py` queries MediaWiki `imageinfo` in batches of 30 with
`iiurlwidth=96`. It stores the returned `https://media.prts.wiki/...` thumbnail URL only. The
generator retries missing standard portraits with the PRTS `(卫戍协议)` filename suffix and never
downloads thumbnail image bytes. `--thumbnails-only` refreshes URLs in an existing catalog without
requesting aliases or Spine metadata.

## Runtime cache contract

`OperatorThumbnailLoader` creates one request per appearance:

- key: `thumbnail:<char_id>:96`
- relative path: `thumbnails/96/<char_id>.img`
- resource version: the complete thumbnail URL
- per-file limit: `256 KiB`
- simultaneous visible-page loads: at most `2`
- per-thumbnail network wait limit: `15 seconds`, starting after the request obtains a load slot

The loader uses `PrtsResourceService`, so thumbnails participate in the existing LRU capacity and
permanent-retention policy. A returned `OperatorThumbnailAsset` holds a resource lease until it is
disposed. Disposing the loader cancels pending UI waits and releases every asset still owned by the
window. When every waiter for a shared request has left, the resource service cancels the underlying
HTTP request so a stale page cannot keep the two-slot download queue occupied.

Before Unity image decoding, `OperatorThumbnailFile.Inspect` accepts PNG or JPEG headers and rejects
files over `256 KiB` or decoded dimensions above `256 x 256`.

## In-game UI integration

`Ctrl+F8` now renders 20 operator cards per page and a larger selected-operator avatar. The window
creates one `OperatorThumbnailLoader` and keeps returned assets for the current page only. Search is
debounced before thumbnail work starts; search or page changes cancel the page wait and dispose its
textures and leases. Window close disposes the loader. Texture creation and `Texture2D.LoadImage`
stay on Unity's main thread; network and file waiting remain asynchronous. A missing URL,
cancellation, validation failure, or offline miss renders a name placeholder or visible failure
caption.

Changing skin or model starts the existing in-world Spine preview automatically. The 96px avatar
identifies the operator and does not claim to represent the selected skin.

The Mods options page must not create this loader. Thumbnail activity begins only after the user
opens the operator gallery.

## Verification

- `tests/test_operator_catalog_thumbnails.py` mocks MediaWiki and checks 30-title batching, 96px URL
  capture, `(卫戍协议)` fallback names, and URL-only catalog records.
- `tests/run_operator_thumbnail_loader_tests.sh` uses an offline HTTP handler to check request
  identity, cache reuse, PNG/JPEG header dimensions, two-slot scheduling, and propagation of
  close-time cancellation to the underlying request.
- Unity `Texture2D.LoadImage`, pagination lifecycle, and visible rendering remain game-validation
  items for the installed Dev package.
