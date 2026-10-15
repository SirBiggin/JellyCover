using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCover.Api;

/// <summary>
/// Thin GraphQL client for the Hardcover.app API (https://api.hardcover.app/v1/graphql).
/// Every public method returns an empty / null result on a definite miss (not found, bad token, bad query) and logs the reason.
/// Transient failures (rate limit in effect, 5xx, timeout, network) throw <see cref="HardcoverUnavailableException"/> so
/// callers can leave the item untouched instead of falling back to a weaker lookup.
/// </summary>
public sealed class HardcoverClient
{
    /// <summary>
    /// Name of the named <see cref="HttpClient"/> registered for Hardcover traffic.
    /// </summary>
    public const string HttpClientName = "JellyCover.Hardcover";

    private const string GraphQlEndpoint = "https://api.hardcover.app/v1/graphql";

    private const string SearchBooksQuery = @"
query JellyCoverSearchBooks($q: String!, $perPage: Int!, $page: Int!) {
  search(query: $q, query_type: ""Book"", per_page: $perPage, page: $page) {
    results
  }
}";

    private const string SearchAuthorsQuery = @"
query JellyCoverSearchAuthors($q: String!, $perPage: Int!, $page: Int!) {
  search(query: $q, query_type: ""Author"", per_page: $perPage, page: $page) {
    results
  }
}";

    private const string BookByIdQuery = @"
query JellyCoverBook($id: Int!) {
  books(where: {id: {_eq: $id}}, limit: 1) {
    id
    title
    subtitle
    description
    release_date
    release_year
    pages
    rating
    ratings_count
    slug
    literary_type_id
    compilation
    cached_tags
    image { url width height }
    contributions { contribution author { id name slug cached_image } }
    book_series(order_by: [{featured: desc}, {id: asc}]) { position featured details series { id name } }
    default_physical_edition { isbn_10 isbn_13 asin publisher { name } }
    default_ebook_edition { isbn_10 isbn_13 asin publisher { name } }
    default_cover_edition { isbn_10 isbn_13 asin publisher { name } }
  }
}";

    private const string BookCoversQuery = @"
query JellyCoverCovers($id: Int!) {
  books(where: {id: {_eq: $id}}, limit: 1) {
    image { url width height }
    editions(where: {image_id: {_is_null: false}}, order_by: {users_count: desc}, limit: 12) {
      image { url width height }
    }
  }
}";

    private const string BookIdByIsbnQuery = @"
query JellyCoverIsbn($isbn: String!) {
  editions(where: {_or: [{isbn_13: {_eq: $isbn}}, {isbn_10: {_eq: $isbn}}]}, order_by: {users_count: desc}, limit: 1) {
    book_id
  }
}";

    private const string BookIdByAsinQuery = @"
query JellyCoverAsin($asin: String!) {
  editions(where: {asin: {_eq: $asin}}, order_by: {users_count: desc}, limit: 1) {
    book_id
  }
}";

    private const string AuthorByIdQuery = @"
query JellyCoverAuthor($id: Int!) {
  authors(where: {id: {_eq: $id}}, limit: 1) {
    id
    name
    slug
    bio
    born_date
    death_date
    location
    books_count
    image { url width height }
  }
}";

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HardcoverClient> _logger;
    private readonly string _userAgent;
    private int _missingTokenWarned;

    /// <summary>
    /// Initializes a new instance of the <see cref="HardcoverClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{HardcoverClient}"/> interface.</param>
    public HardcoverClient(IHttpClientFactory httpClientFactory, ILogger<HardcoverClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        var version = typeof(HardcoverClient).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        _userAgent = string.Format(CultureInfo.InvariantCulture, "JellyCover/{0} (Jellyfin plugin; +https://github.com/SirBiggin/JellyCover)", version);
    }

    /// <summary>
    /// Gets a value indicating whether an API token has been configured.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConfiguredToken);

    /// <summary>
    /// Gets a value indicating whether requests are currently being skipped because Hardcover's rate limit was exceeded.
    /// </summary>
    public bool IsPaused => HardcoverRateLimitHandler.PausedUntil is not null;

    private static string? ConfiguredToken => NormalizeToken(Plugin.Instance?.Configuration.ApiToken);

    /// <summary>
    /// Normalizes a pasted token: trims whitespace and strips a leading "Bearer ".
    /// </summary>
    /// <param name="token">The raw token.</param>
    /// <returns>The bare token, or null if empty.</returns>
    public static string? NormalizeToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var t = token.Trim();
        if (t.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            t = t.Substring(7).Trim();
        }

        return t.Length == 0 ? null : t;
    }

    /// <summary>
    /// Searches Hardcover books.
    /// </summary>
    /// <param name="query">Free text query (title, ISBN, author...).</param>
    /// <param name="perPage">Maximum hits.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hits in relevance order.</returns>
    public async Task<IReadOnlyList<HardcoverBookSearchHit>> SearchBooksAsync(string query, int perPage, CancellationToken cancellationToken)
    {
        var hits = new List<HardcoverBookSearchHit>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return hits;
        }

        using var doc = await ExecuteAsync(SearchBooksQuery, new { q = query.Trim(), perPage, page = 1 }, null, cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return hits;
        }

        foreach (var document in EnumerateSearchDocuments(doc.RootElement))
        {
            var id = GetInt(document, "id");
            if (id is null)
            {
                continue;
            }

            var hit = new HardcoverBookSearchHit
            {
                Id = id.Value,
                Title = GetString(document, "title") ?? string.Empty,
                Subtitle = GetString(document, "subtitle"),
                Description = GetString(document, "description"),
                ReleaseYear = GetInt(document, "release_year"),
                Slug = GetString(document, "slug"),
                Rating = GetDouble(document, "rating"),
                UsersCount = GetInt(document, "users_count") ?? 0,
                ImageUrl = ParseImage(document, "image")?.Url
            };
            AddAll(hit.AuthorNames, GetStringArray(document, "author_names"));
            AddAll(hit.SeriesNames, GetStringArray(document, "series_names"));
            AddAll(hit.Isbns, GetStringArray(document, "isbns"));

            if (hit.AuthorNames.Count == 0 && document.TryGetProperty("contributions", out var contributions) && contributions.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in contributions.EnumerateArray())
                {
                    if (c.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object)
                    {
                        var name = GetString(author, "name");
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            hit.AuthorNames.Add(name);
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(hit.Title))
            {
                hits.Add(hit);
            }
        }

        _logger.LogDebug("Hardcover book search for \"{Query}\" returned {Count} hits", query, hits.Count);
        return hits;
    }

    /// <summary>
    /// Searches Hardcover authors.
    /// </summary>
    /// <param name="query">Author name.</param>
    /// <param name="perPage">Maximum hits.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hits in relevance order.</returns>
    public async Task<IReadOnlyList<HardcoverAuthorSearchHit>> SearchAuthorsAsync(string query, int perPage, CancellationToken cancellationToken)
    {
        var hits = new List<HardcoverAuthorSearchHit>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return hits;
        }

        using var doc = await ExecuteAsync(SearchAuthorsQuery, new { q = query.Trim(), perPage, page = 1 }, null, cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return hits;
        }

        foreach (var document in EnumerateSearchDocuments(doc.RootElement))
        {
            var id = GetInt(document, "id");
            var name = GetString(document, "name");
            if (id is null || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var hit = new HardcoverAuthorSearchHit
            {
                Id = id.Value,
                Name = name,
                Slug = GetString(document, "slug"),
                BooksCount = GetInt(document, "books_count") ?? 0,
                ImageUrl = ParseImage(document, "image")?.Url
            };
            AddAll(hit.AlternateNames, GetStringArray(document, "alternate_names"));
            AddAll(hit.Books, GetStringArray(document, "books"));
            hits.Add(hit);
        }

        _logger.LogDebug("Hardcover author search for \"{Query}\" returned {Count} hits", query, hits.Count);
        return hits;
    }

    /// <summary>
    /// Resolves an ISBN-10 / ISBN-13 to a Hardcover book id.
    /// </summary>
    /// <param name="isbn">The ISBN (hyphens / spaces are ignored).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The book id, or null.</returns>
    public async Task<int?> FindBookIdByIsbnAsync(string isbn, CancellationToken cancellationToken)
    {
        var clean = new string(isbn.Where(c => char.IsDigit(c) || c == 'X' || c == 'x').ToArray()).ToUpperInvariant();
        if (clean.Length != 10 && clean.Length != 13)
        {
            return null;
        }

        using var doc = await ExecuteAsync(BookIdByIsbnQuery, new { isbn = clean }, null, cancellationToken).ConfigureAwait(false);
        if (doc is null || !TryGetData(doc.RootElement, "editions", out var editions) || editions.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var edition in editions.EnumerateArray())
        {
            var bookId = GetInt(edition, "book_id");
            if (bookId is not null)
            {
                return bookId;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves an Amazon ASIN (e.g. from a Calibre OPF's MOBI-ASIN identifier) to a Hardcover book id via its editions.
    /// </summary>
    /// <param name="asin">The ASIN.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The book id, or null.</returns>
    public async Task<int?> FindBookIdByAsinAsync(string asin, CancellationToken cancellationToken)
    {
        var clean = asin.Trim().ToUpperInvariant();
        if (clean.Length != 10)
        {
            return null;
        }

        using var doc = await ExecuteAsync(BookIdByAsinQuery, new { asin = clean }, null, cancellationToken).ConfigureAwait(false);
        if (doc is null || !TryGetData(doc.RootElement, "editions", out var editions) || editions.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var edition in editions.EnumerateArray())
        {
            var bookId = GetInt(edition, "book_id");
            if (bookId is not null)
            {
                return bookId;
            }
        }

        return null;
    }

    /// <summary>
    /// Fetches full book details.
    /// </summary>
    /// <param name="id">Hardcover book id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The book, or null.</returns>
    public async Task<HardcoverBook?> GetBookAsync(int id, CancellationToken cancellationToken)
    {
        using var doc = await ExecuteAsync(BookByIdQuery, new { id }, null, cancellationToken).ConfigureAwait(false);
        if (doc is null || !TryGetData(doc.RootElement, "books", out var books) || books.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var b in books.EnumerateArray())
        {
            return ParseBook(b);
        }

        _logger.LogInformation("Hardcover book {Id} not found", id);
        return null;
    }

    /// <summary>
    /// Fetches the cover candidates for a book: the book's own cover first, then the covers of its most popular editions.
    /// </summary>
    /// <param name="id">Hardcover book id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Distinct images.</returns>
    public async Task<IReadOnlyList<HardcoverImage>> GetBookCoversAsync(int id, CancellationToken cancellationToken)
    {
        var images = new List<HardcoverImage>();
        using var doc = await ExecuteAsync(BookCoversQuery, new { id }, null, cancellationToken).ConfigureAwait(false);
        if (doc is null || !TryGetData(doc.RootElement, "books", out var books) || books.ValueKind != JsonValueKind.Array)
        {
            return images;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in books.EnumerateArray())
        {
            var primary = ParseImage(b, "image");
            if (primary is not null && seen.Add(primary.Url))
            {
                images.Add(primary);
            }

            if (b.TryGetProperty("editions", out var editions) && editions.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in editions.EnumerateArray())
                {
                    var img = ParseImage(e, "image");
                    if (img is not null && seen.Add(img.Url))
                    {
                        images.Add(img);
                    }
                }
            }
        }

        return images;
    }

    /// <summary>
    /// Fetches full author details.
    /// </summary>
    /// <param name="id">Hardcover author id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The author, or null.</returns>
    public async Task<HardcoverAuthor?> GetAuthorAsync(int id, CancellationToken cancellationToken)
    {
        using var doc = await ExecuteAsync(AuthorByIdQuery, new { id }, null, cancellationToken).ConfigureAwait(false);
        if (doc is null || !TryGetData(doc.RootElement, "authors", out var authors) || authors.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var a in authors.EnumerateArray())
        {
            var name = GetString(a, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            return new HardcoverAuthor
            {
                Id = GetInt(a, "id") ?? id,
                Name = name,
                Slug = GetString(a, "slug"),
                Bio = GetString(a, "bio"),
                BornDate = GetDate(a, "born_date"),
                DeathDate = GetDate(a, "death_date"),
                Location = GetString(a, "location"),
                BooksCount = GetInt(a, "books_count") ?? 0,
                Image = ParseImage(a, "image")
            };
        }

        return null;
    }

    /// <summary>
    /// Downloads an image (cover / author photo) through the plugin's HTTP client.
    /// </summary>
    /// <param name="url">The image url.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The HTTP response.</returns>
    public async Task<HttpResponseMessage> GetImageAsync(string url, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(_userAgent);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a real catalog search to verify the token works and has the <c>read:catalog</c> scope.
    /// </summary>
    /// <param name="tokenOverride">Token to test instead of the saved one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome.</returns>
    public async Task<ConnectionTestResult> TestConnectionAsync(string? tokenOverride, CancellationToken cancellationToken)
    {
        var token = NormalizeToken(tokenOverride) ?? ConfiguredToken;
        if (token is null)
        {
            return new ConnectionTestResult { Ok = false, Message = "No API token configured." };
        }

        try
        {
            // a manual test should always reach the API; if the limit is still exceeded the real 429 re-pauses
            HardcoverRateLimitHandler.ClearPause();
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = BuildRequest(SearchBooksQuery, new { q = "dune", perPage = 1, page = 1 }, token);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var rateLimit = response.Headers.TryGetValues("RateLimit", out var values) ? string.Join(", ", values) : null;

            if (!response.IsSuccessStatusCode)
            {
                return new ConnectionTestResult
                {
                    Ok = false,
                    Message = string.Format(CultureInfo.InvariantCulture, "HTTP {0} {1}: {2}", (int)response.StatusCode, response.ReasonPhrase, DescribeErrorBody(body)),
                    RateLimit = rateLimit
                };
            }

            using var doc = JsonDocument.Parse(body);
            var graphQlError = ExtractGraphQlError(doc.RootElement);
            if (graphQlError is not null)
            {
                return new ConnectionTestResult { Ok = false, Message = "GraphQL error: " + graphQlError, RateLimit = rateLimit };
            }

            var first = EnumerateSearchDocuments(doc.RootElement).FirstOrDefault();
            var title = first.ValueKind == JsonValueKind.Object ? GetString(first, "title") : null;
            return new ConnectionTestResult
            {
                Ok = true,
                Message = title is null
                    ? "Connected, but the test search returned no hits (token works)."
                    : string.Format(CultureInfo.InvariantCulture, "Connected. Test search returned \"{0}\".", title),
                RateLimit = rateLimit
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hardcover connection test failed");
            return new ConnectionTestResult { Ok = false, Message = ex.Message };
        }
    }

    /// <summary>
    /// Executes a GraphQL query. Returns the parsed response document (caller disposes) or null on any failure.
    /// </summary>
    private async Task<JsonDocument?> ExecuteAsync(string query, object variables, string? tokenOverride, CancellationToken cancellationToken)
    {
        var token = tokenOverride ?? ConfiguredToken;
        if (token is null)
        {
            if (Interlocked.Exchange(ref _missingTokenWarned, 1) == 0)
            {
                _logger.LogWarning("JellyCover: no Hardcover API token configured. Set one in Dashboard > Plugins > JellyCover.");
            }

            return null;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = BuildRequest(query, variables, token);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var detail = DescribeErrorBody(body);
                if (detail.Length == 0)
                {
                    detail = response.ReasonPhrase ?? string.Empty;
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _logger.LogError("Hardcover rejected the API token (401). Check it in Dashboard > Plugins > JellyCover. {Detail}", detail);
                    return null;
                }

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    _logger.LogError("Hardcover refused the request (403). The token probably lacks the read:catalog scope. {Detail}", detail);
                    return null;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    // the rate-limit handler has already logged the pause (once); keep per-item noise at debug
                    _logger.LogDebug("Hardcover request skipped by the rate limit: {Detail}", detail);
                    throw new HardcoverUnavailableException("Hardcover rate limit: " + detail);
                }

                if ((int)response.StatusCode >= 500)
                {
                    _logger.LogWarning("Hardcover is unavailable (HTTP {Status}): {Detail}", (int)response.StatusCode, detail);
                    throw new HardcoverUnavailableException(string.Format(CultureInfo.InvariantCulture, "Hardcover HTTP {0}: {1}", (int)response.StatusCode, detail));
                }

                _logger.LogWarning("Hardcover request failed with HTTP {Status}: {Detail}", (int)response.StatusCode, detail);
                return null;
            }

            var doc = JsonDocument.Parse(body);
            var error = ExtractGraphQlError(doc.RootElement);
            if (error is not null)
            {
                _logger.LogWarning("Hardcover GraphQL error: {Error}", error);
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                {
                    doc.Dispose();
                    return null;
                }
            }

            return doc;
        }
        catch (HardcoverUnavailableException)
        {
            throw;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Hardcover request timed out");
            throw new HardcoverUnavailableException("Hardcover request timed out", ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Hardcover could not be reached");
            throw new HardcoverUnavailableException("Hardcover could not be reached: " + ex.Message, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hardcover request failed");
            return null;
        }
    }

    private HttpRequestMessage BuildRequest(string query, object variables, string token)
    {
        var payload = JsonSerializer.Serialize(new { query, variables }, _jsonOptions);
        var request = new HttpRequestMessage(HttpMethod.Post, GraphQlEndpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd(_userAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static string DescribeErrorBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                var parts = new List<string>();
                foreach (var key in new[] { "error", "error_description", "message", "scope" })
                {
                    var v = GetString(root, key);
                    if (!string.IsNullOrWhiteSpace(v))
                    {
                        parts.Add(key + "=" + v);
                    }
                }

                var gql = ExtractGraphQlError(root);
                if (gql is not null)
                {
                    parts.Add(gql);
                }

                if (parts.Count > 0)
                {
                    return string.Join("; ", parts);
                }
            }
        }
        catch (JsonException)
        {
            // fall through to raw text
        }

        return body.Length > 300 ? body.Substring(0, 300) : body;
    }

    private static string? ExtractGraphQlError(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("errors", out var errors))
        {
            return null;
        }

        var messages = new List<string>();
        if (errors.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in errors.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String)
                {
                    messages.Add(e.GetString() ?? string.Empty);
                }
                else if (e.ValueKind == JsonValueKind.Object)
                {
                    messages.Add(GetString(e, "message") ?? e.ToString());
                }
            }
        }
        else if (errors.ValueKind == JsonValueKind.String)
        {
            messages.Add(errors.GetString() ?? string.Empty);
        }

        return messages.Count == 0 ? null : string.Join(" | ", messages);
    }

    private static bool TryGetData(JsonElement root, string field, out JsonElement value)
    {
        value = default;
        return root.ValueKind == JsonValueKind.Object
               && root.TryGetProperty("data", out var data)
               && data.ValueKind == JsonValueKind.Object
               && data.TryGetProperty(field, out value);
    }

    /// <summary>
    /// Walks data.search.results.hits[].document. The <c>results</c> scalar is normally an inline JSON object,
    /// but a JSON-encoded string is tolerated too.
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateSearchDocuments(JsonElement root)
    {
        if (!TryGetData(root, "search", out var search) || search.ValueKind != JsonValueKind.Object
            || !search.TryGetProperty("results", out var results))
        {
            yield break;
        }

        JsonDocument? parsed = null;
        try
        {
            if (results.ValueKind == JsonValueKind.String)
            {
                var text = results.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    yield break;
                }

                parsed = JsonDocument.Parse(text);
                results = parsed.RootElement;
            }

            if (results.ValueKind != JsonValueKind.Object || !results.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var hit in hits.EnumerateArray())
            {
                if (hit.ValueKind == JsonValueKind.Object && hit.TryGetProperty("document", out var document) && document.ValueKind == JsonValueKind.Object)
                {
                    yield return document;
                }
            }
        }
        finally
        {
            parsed?.Dispose();
        }
    }

    private HardcoverBook ParseBook(JsonElement b)
    {
        var book = new HardcoverBook
        {
            Id = GetInt(b, "id") ?? 0,
            Title = GetString(b, "title") ?? string.Empty,
            Subtitle = GetString(b, "subtitle"),
            Description = GetString(b, "description"),
            ReleaseDate = GetDate(b, "release_date"),
            ReleaseYear = GetInt(b, "release_year"),
            Pages = GetInt(b, "pages"),
            Rating = GetDouble(b, "rating"),
            RatingsCount = GetInt(b, "ratings_count") ?? 0,
            Slug = GetString(b, "slug"),
            LiteraryTypeId = GetInt(b, "literary_type_id"),
            Compilation = b.TryGetProperty("compilation", out var comp) && comp.ValueKind == JsonValueKind.True,
            Image = ParseImage(b, "image")
        };

        if (book.ReleaseYear is null && book.ReleaseDate is not null)
        {
            book.ReleaseYear = book.ReleaseDate.Value.Year;
        }

        // Tags: cached_tags is a jsonb blob keyed by category ("Genre", "Mood", "Content Warning", "Tag"),
        // each an array of {tag, tagSlug, category, count...}. Be tolerant of other shapes.
        if (b.TryGetProperty("cached_tags", out var tags))
        {
            ParseTags(tags, book);
        }

        if (b.TryGetProperty("contributions", out var contributions) && contributions.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in contributions.EnumerateArray())
            {
                if (!c.TryGetProperty("author", out var author) || author.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = GetString(author, "name");
                var authorId = GetInt(author, "id");
                if (string.IsNullOrWhiteSpace(name) || authorId is null)
                {
                    continue;
                }

                book.Contributors.Add(new HardcoverContributor
                {
                    AuthorId = authorId.Value,
                    Name = name.Trim(),
                    Slug = GetString(author, "slug"),
                    Role = GetString(c, "contribution"),
                    ImageUrl = ParseImage(author, "cached_image")?.Url
                });
            }
        }

        if (b.TryGetProperty("book_series", out var bookSeries) && bookSeries.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in bookSeries.EnumerateArray())
            {
                if (!s.TryGetProperty("series", out var series) || series.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = GetString(series, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                book.Series.Add(new HardcoverSeriesEntry
                {
                    SeriesId = GetInt(series, "id") ?? 0,
                    Name = name.Trim(),
                    Position = GetDouble(s, "position"),
                    Details = GetString(s, "details"),
                    Featured = s.TryGetProperty("featured", out var f) && f.ValueKind == JsonValueKind.True
                });
            }

            // featured first (query already orders, but be safe)
            var ordered = book.Series.OrderByDescending(s => s.Featured).ToList();
            book.Series.Clear();
            AddAll(book.Series, ordered);
        }

        foreach (var editionField in new[] { "default_physical_edition", "default_ebook_edition", "default_cover_edition" })
        {
            if (!b.TryGetProperty(editionField, out var edition) || edition.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            book.Isbn13 ??= NullIfBlank(GetString(edition, "isbn_13"));
            book.Isbn10 ??= NullIfBlank(GetString(edition, "isbn_10"));
            book.Asin ??= NullIfBlank(GetString(edition, "asin"));
            if (book.Publisher is null && edition.TryGetProperty("publisher", out var publisher) && publisher.ValueKind == JsonValueKind.Object)
            {
                book.Publisher = NullIfBlank(GetString(publisher, "name"));
            }
        }

        _logger.LogDebug(
            "Parsed Hardcover book {Id} \"{Title}\": {Contributors} contributors, {Series} series, {Genres} genres",
            book.Id,
            book.Title,
            book.Contributors.Count,
            book.Series.Count,
            book.Genres.Count);
        return book;
    }

    private static void ParseTags(JsonElement tags, HardcoverBook book)
    {
        if (tags.ValueKind == JsonValueKind.String)
        {
            var text = tags.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            try
            {
                using var inner = JsonDocument.Parse(text);
                ParseTags(inner.RootElement.Clone(), book);
            }
            catch (JsonException)
            {
                // ignore malformed cache blob
            }

            return;
        }

        if (tags.ValueKind == JsonValueKind.Object)
        {
            foreach (var category in tags.EnumerateObject())
            {
                var target = TargetListForCategory(category.Name, book);
                if (target is null || category.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in category.Value.EnumerateArray())
                {
                    AddTag(target, TagName(item));
                }
            }
        }
        else if (tags.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in tags.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var target = TargetListForCategory(GetString(item, "category") ?? GetString(item, "categorySlug") ?? "Tag", book);
                if (target is not null)
                {
                    AddTag(target, TagName(item));
                }
            }
        }
    }

    private static ICollection<string>? TargetListForCategory(string category, HardcoverBook book)
    {
        var c = category.Replace("_", " ", StringComparison.Ordinal).Replace("-", " ", StringComparison.Ordinal).Trim();
        if (c.Equals("Genre", StringComparison.OrdinalIgnoreCase) || c.Equals("Genres", StringComparison.OrdinalIgnoreCase))
        {
            return book.Genres;
        }

        if (c.Equals("Mood", StringComparison.OrdinalIgnoreCase) || c.Equals("Moods", StringComparison.OrdinalIgnoreCase))
        {
            return book.Moods;
        }

        if (c.Equals("Content Warning", StringComparison.OrdinalIgnoreCase) || c.Equals("Content Warnings", StringComparison.OrdinalIgnoreCase))
        {
            return book.ContentWarnings;
        }

        return null;
    }

    private static string? TagName(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            return item.GetString();
        }

        if (item.ValueKind == JsonValueKind.Object)
        {
            return GetString(item, "tag") ?? GetString(item, "name") ?? GetString(item, "tagSlug");
        }

        return null;
    }

    private static void AddTag(ICollection<string> target, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var v = value.Trim();
        if (!target.Contains(v, StringComparer.OrdinalIgnoreCase))
        {
            target.Add(v);
        }
    }

    private static HardcoverImage? ParseImage(JsonElement parent, string field)
    {
        if (!parent.TryGetProperty(field, out var image))
        {
            return null;
        }

        if (image.ValueKind == JsonValueKind.String)
        {
            var text = image.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (text.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return new HardcoverImage { Url = text };
            }

            try
            {
                using var inner = JsonDocument.Parse(text);
                return ParseImageObject(inner.RootElement);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return ParseImageObject(image);
    }

    private static HardcoverImage? ParseImageObject(JsonElement image)
    {
        if (image.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var url = GetString(image, "url");
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new HardcoverImage
        {
            Url = url,
            Width = GetInt(image, "width"),
            Height = GetInt(image, "height")
        };
    }

    private static void AddAll<T>(ICollection<T> target, IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? GetString(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static int? GetInt(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
        {
            return null;
        }

        if (v.ValueKind == JsonValueKind.Number)
        {
            if (v.TryGetInt32(out var i))
            {
                return i;
            }

            if (v.TryGetDouble(out var d))
            {
                return (int)d;
            }

            return null;
        }

        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static double? GetDouble(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
        {
            return null;
        }

        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d))
        {
            return d;
        }

        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DateTime? GetDate(JsonElement e, string name)
    {
        var s = GetString(e, name);
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var exact))
        {
            return exact;
        }

        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var loose))
        {
            return loose;
        }

        return null;
    }

    private static IEnumerable<string> GetStringArray(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
        {
            yield break;
        }

        if (v.ValueKind == JsonValueKind.String)
        {
            var single = v.GetString();
            if (!string.IsNullOrWhiteSpace(single))
            {
                yield return single;
            }

            yield break;
        }

        if (v.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    yield return s;
                }
            }
        }
    }
}
