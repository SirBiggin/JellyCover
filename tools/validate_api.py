#!/usr/bin/env python3
"""Run every GraphQL query JellyCover uses against the live Hardcover API and print what comes back.

Usage (token is never written anywhere):
  set HARDCOVER_TOKEN=...   (or $env:HARDCOVER_TOKEN / export)
  python tools/validate_api.py [search title] [isbn] [asin]

Checks: search (Book + Author), book by id, covers, isbn -> book id, asin -> book id, author by id.
Exits non-zero if any query returns GraphQL errors, so field-name drift in the beta API shows up immediately.
"""
import json
import os
import re
import sys
import urllib.request

ENDPOINT = "https://api.hardcover.app/v1/graphql"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CLIENT = os.path.join(ROOT, "Jellyfin.Plugin.JellyCover", "Api", "HardcoverClient.cs")

token = os.environ.get("HARDCOVER_TOKEN", "").strip()
if token.lower().startswith("bearer "):
    token = token[7:].strip()
if not token:
    sys.exit("set HARDCOVER_TOKEN first")

title = sys.argv[1] if len(sys.argv) > 1 else "The Way of Kings"
isbn = sys.argv[2] if len(sys.argv) > 2 else "9780765326355"

# pull the exact query strings out of the C# source so this never drifts from the plugin
src = open(CLIENT, encoding="utf-8").read()
queries = {m.group(1): m.group(2).replace('""', '"') for m in re.finditer(r'private const string (\w+Query) = @"(.*?)";', src, re.S)}
print("queries found in HardcoverClient.cs:", ", ".join(queries))

failed = False


def run(name, variables):
    global failed
    body = json.dumps({"query": queries[name], "variables": variables}).encode()
    req = urllib.request.Request(ENDPOINT, data=body, method="POST", headers={
        "Content-Type": "application/json",
        "Authorization": f"Bearer {token}",
        "User-Agent": "JellyCover/validate (plugin query check)",
    })
    try:
        with urllib.request.urlopen(req, timeout=40) as resp:
            data = json.loads(resp.read())
            rl = resp.headers.get("RateLimit")
    except urllib.error.HTTPError as e:
        failed = True
        print(f"\n== {name} -> HTTP {e.code}: {e.read()[:400]!r}")
        return None
    print(f"\n== {name} {variables}  [RateLimit: {rl}]")
    if "errors" in data:
        failed = True
        print("  GRAPHQL ERRORS:", json.dumps(data["errors"], indent=2)[:1500])
    return data.get("data")


d = run("SearchBooksQuery", {"q": title, "perPage": 3, "page": 1})
book_id = None
if d:
    results = d["search"]["results"]
    if isinstance(results, str):
        results = json.loads(results)
    hits = results.get("hits", [])
    print(f"  found={results.get('found')} hits={len(hits)}")
    for h in hits:
        doc = h["document"]
        print(f"  - id={doc.get('id')} title={doc.get('title')!r} year={doc.get('release_year')} authors={doc.get('author_names')} "
              f"series={doc.get('series_names')} image={'yes' if doc.get('image') else 'no'}")
    if hits:
        book_id = int(hits[0]["document"]["id"])
        print("  document keys:", sorted(hits[0]["document"].keys()))

d = run("BookIdByIsbnQuery", {"isbn": isbn})
if d:
    print("  editions:", d["editions"])
    if d["editions"] and not book_id:
        book_id = d["editions"][0]["book_id"]

asin = sys.argv[3] if len(sys.argv) > 3 else "B00A820Y6U"  # Hellboy Vol. 1 (Kindle)
d = run("BookIdByAsinQuery", {"asin": asin})
if d:
    print("  editions:", d["editions"])

author_id = None
if book_id:
    d = run("BookByIdQuery", {"id": book_id})
    if d and d["books"]:
        b = d["books"][0]
        print(f"  title={b.get('title')!r} date={b.get('release_date')} pages={b.get('pages')} rating={b.get('rating')} ({b.get('ratings_count')})")
        print(f"  image={b.get('image')}")
        print(f"  contributions={[(c.get('contribution'), c['author']['name'], c['author']['id']) for c in b.get('contributions', [])]}")
        print(f"  book_series={b.get('book_series')}")
        for k in ("default_physical_edition", "default_ebook_edition", "default_cover_edition"):
            print(f"  {k}={b.get(k)}")
        tags = b.get("cached_tags")
        if isinstance(tags, str):
            tags = json.loads(tags)
        if isinstance(tags, dict):
            print("  cached_tags categories:", {k: len(v) if isinstance(v, list) else type(v).__name__ for k, v in tags.items()})
            for k, v in tags.items():
                if isinstance(v, list) and v:
                    print(f"    {k}[0] = {json.dumps(v[0])[:200]}")
        else:
            print("  cached_tags:", json.dumps(tags)[:400])
        contribs = b.get("contributions") or []
        if contribs:
            author_id = contribs[0]["author"]["id"]
            print("  author cached_image:", json.dumps(contribs[0]["author"].get("cached_image"))[:200])

    d = run("BookCoversQuery", {"id": book_id})
    if d and d["books"]:
        b = d["books"][0]
        print(f"  book image={b.get('image')}")
        print(f"  edition covers={len(b.get('editions', []))}")

d = run("SearchAuthorsQuery", {"q": "Brandon Sanderson", "perPage": 2, "page": 1})
if d:
    results = d["search"]["results"]
    if isinstance(results, str):
        results = json.loads(results)
    for h in results.get("hits", []):
        doc = h["document"]
        print(f"  - id={doc.get('id')} name={doc.get('name')!r} books_count={doc.get('books_count')} image={'yes' if doc.get('image') else 'no'} alt={doc.get('alternate_names')}")
        author_id = author_id or int(doc["id"])

if author_id:
    d = run("AuthorByIdQuery", {"id": author_id})
    if d and d["authors"]:
        a = d["authors"][0]
        print(f"  name={a.get('name')!r} born={a.get('born_date')} died={a.get('death_date')} location={a.get('location')} books={a.get('books_count')} image={'yes' if a.get('image') else 'no'}")
        print(f"  bio={ (a.get('bio') or '')[:160]!r}")

print("\nRESULT:", "FAILED - see errors above" if failed else "all queries OK")
sys.exit(1 if failed else 0)
