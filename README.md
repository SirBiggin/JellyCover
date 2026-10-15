# JellyCover

Hardcover.app metadata provider for Jellyfin book libraries. Same idea as the official
[OpenLibrary plugin](https://github.com/jellyfin/jellyfin-plugin-openlibrary), but backed by the
[Hardcover GraphQL API](https://docs.hardcover.app/api/getting-started/) and with cover images.

## What it provides

**Books**

- Title (optionally with subtitle) and description
- Release date / year
- Genres (Hardcover "Genre" tags), moods and optional content warnings as tags
- Community rating (Hardcover's 0–5 average scaled to 0–10)
- Series name and position (`IndexNumber`)
- Publisher (as studio), ISBN and ASIN of the default edition
- Contributors as people: authors, illustrators, translators, narrators, editors, cover artists, …
- Cover images: the book's cover plus the covers of its most popular editions
- A **Hardcover** external id + link on every matched book (`https://hardcover.app/id/book/<id>`)

**Authors (Person items)**

- Biography, birth and death dates, location, photo
- Only *exact* name matches are used, because Jellyfin people are shared with movie / TV libraries

## Requirements

- Jellyfin **12.2** or newer (`targetAbi` 12.2.0.0, .NET 10)
- A free [Hardcover](https://hardcover.app) account and a personal access token with the **`read:catalog`** scope

## Install

### From the plugin catalog (recommended)

1. Dashboard → Plugins → **Repositories** → **+** and add:

   ```
   https://raw.githubusercontent.com/SirBiggin/jellyfin-plugin-repo/main/manifest.json
   ```

2. Dashboard → Plugins → **Catalog** → **Metadata** → **JellyCover** → Install.
   (Hard-reload the browser if the catalog does not show it right after adding the repository; the web UI caches the package list.)
3. Restart Jellyfin.

### Manual

1. Download `jellycover_<version>.zip` from the [releases page](https://github.com/SirBiggin/JellyCover/releases).
2. Unzip it into `<jellyfin data dir>/plugins/JellyCover_<version>/`
   (Debian package: `/var/lib/jellyfin/plugins/JellyCover_1.0.0.0/`). The folder must contain
   `Jellyfin.Plugin.JellyCover.dll` and `meta.json`, owned by the `jellyfin` user.
3. Restart Jellyfin.

## Configure

1. Create a token: <https://hardcover.app/account/api/keys/new?scope=read:catalog>
   (that link pre-selects the scope). Choose a long expiration; the plugin stops working when it expires.
2. Dashboard → Plugins → **JellyCover** → paste the token → **Test connection** → **Save**.
   The test runs a real catalog search, so it also proves the scope is right.
3. Library → your Book library → Manage library:
   - **Metadata downloaders (Book)**: tick **Hardcover** and move it to the top.
   - **Image fetchers (Book)**: tick **Hardcover**.
   - Optionally the same under the **Person** section for author bios and photos.
4. Scan the library, or use **Identify** on a book to pick the exact Hardcover record
   (you can also type a Hardcover book id into the Identify dialog).

### Options

| Option | Default | Notes |
|---|---|---|
| Offer cover images | on | Book cover + up to 12 edition covers in the image picker |
| Import genres / max genres | on / 10 | Hardcover "Genre" tags, most-used first |
| Import moods as tags | on | Hardcover "Mood" tags |
| Import content warnings as tags | off | Hardcover "Content Warning" tags |
| Import Hardcover rating | on | 0–5 average × 2, only when the book has ratings |
| Import series name and position | on | Featured series first |
| Import publisher | on | Stored as a studio |
| Import contributors | on | Authors, illustrators, translators, narrators, editors, cover artists… |
| Append subtitle to the title | off | `Title: Subtitle` |
| Match by title search | on | Off = only Hardcover id / ISBN matches during scans |
| Minimum title match score | 60 | 0–100; 100 = exact title only |
| Fetch author biographies / photos | on / on | Exact name matches only |

## How matching works

1. A **Hardcover id** on the item (set by Identify, or an earlier match) → fetched directly.
2. Otherwise an **ISBN** on the item (the Bookshelf plugin's OPF reader stores `ISBN`) → resolved via Hardcover's editions.
3. Otherwise a **title search**. File-name noise such as `(2001)` and `[tags]` is stripped first. The best hit
   is accepted only if its title similarity (plus year / series bonuses) reaches **Minimum match score**.
   Below that the book is left untouched rather than guessed.

Hardcover's search is Typesense-backed and returns relevance-sorted hits; ties go to Hardcover's ranking.

## Rate limits

Hardcover's free plan allows a burst of 10 requests, 60 per minute and 5,000 per day. The plugin throttles
itself (token bucket of 8, refilling 1/s) and honours `Retry-After` once on a 429. A full scan uses roughly
two requests per book (search + details) and one more per cover fetch, so a very large first scan can hit
the daily cap; later scans only touch new or refreshed items. Errors are logged under
`Jellyfin.Plugin.JellyCover`, never thrown into the scan.

## Build

```
dotnet build Jellyfin.Plugin.JellyCover/Jellyfin.Plugin.JellyCover.csproj -c Release
python package.py            # → dist/jellycover_<version>.zip + dist/manifest-entry.json
```

Requires the .NET 10 SDK. The project compiles against `Jellyfin.Controller` / `Jellyfin.Model` 12.2.0
with the Jellyfin analyzer ruleset and warnings as errors.

`tools/validate_api.py` runs every GraphQL query the plugin uses against the live API
(`HARDCOVER_TOKEN` env var) and fails on any schema drift; handy since the Hardcover API is still in beta.

## Publishing a release

```
python package.py --version 1.0.1.0 --changelog "..."
gh release create v1.0.1.0 dist/jellycover_1.0.1.0.zip --title "JellyCover 1.0.1.0" --notes "..."
```

Then merge `dist/manifest-entry.json` into the plugin repository's `manifest.json`
(`checksum` is the zip's MD5, which Jellyfin verifies on install).

## Notes

- Hardcover asks that scripts send a descriptive `User-Agent`; the plugin identifies itself as `JellyCover/<version>`.
- Images on Hardcover are user-uploaded. For a private home server that is fine; see Hardcover's
  [public data warning](https://docs.hardcover.app/api/getting-started/#public-data-warning) before exposing them publicly.
- Not affiliated with Hardcover or the Jellyfin project.

## License

GPL-3.0, like the Jellyfin plugins it is modelled on.
