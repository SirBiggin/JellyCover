using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCover.Api;
using Jellyfin.Plugin.JellyCover.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCover.Providers;

/// <summary>
/// Book metadata provider backed by Hardcover.app.
/// </summary>
public class JellyCoverBookProvider : IRemoteMetadataProvider<Book, BookInfo>, IHasOrder
{
    private const int SearchResultLimit = 10;

    private readonly HardcoverClient _client;
    private readonly ILogger<JellyCoverBookProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyCoverBookProvider"/> class.
    /// </summary>
    /// <param name="client">The Hardcover client.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{JellyCoverBookProvider}"/> interface.</param>
    public JellyCoverBookProvider(HardcoverClient client, ILogger<JellyCoverBookProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => Plugin.ProviderName;

    /// <inheritdoc />
    public int Order => 1;

    private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BookInfo searchInfo, CancellationToken cancellationToken)
    {
        var results = new List<RemoteSearchResult>();
        if (!_client.IsConfigured)
        {
            return results;
        }

        // Identify dialog with an explicit Hardcover id typed in
        var explicitId = GetHardcoverId(searchInfo.ProviderIds);
        if (explicitId is not null)
        {
            var book = await _client.GetBookAsync(explicitId.Value, cancellationToken).ConfigureAwait(false);
            if (book is not null)
            {
                results.Add(ToSearchResult(book));
                return results;
            }
        }

        var isbn = GetIsbn(searchInfo.ProviderIds);
        if (isbn is not null)
        {
            var id = await _client.FindBookIdByIsbnAsync(isbn, cancellationToken).ConfigureAwait(false);
            if (id is not null)
            {
                var book = await _client.GetBookAsync(id.Value, cancellationToken).ConfigureAwait(false);
                if (book is not null)
                {
                    results.Add(ToSearchResult(book));
                }
            }
        }

        if (string.IsNullOrWhiteSpace(searchInfo.Name))
        {
            return results;
        }

        var query = Matching.CleanForSearch(searchInfo.Name);
        var hits = await _client.SearchBooksAsync(query, SearchResultLimit, cancellationToken).ConfigureAwait(false);
        foreach (var hit in hits)
        {
            if (results.Any(r => string.Equals(r.GetProviderId(Plugin.ProviderIdKey), hit.Id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)))
            {
                continue;
            }

            results.Add(ToSearchResult(hit));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<MetadataResult<Book>> GetMetadata(BookInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Book>();
        if (!_client.IsConfigured)
        {
            return result;
        }

        try
        {
            HardcoverBook? book = null;
            var queriedById = false;

            var id = GetHardcoverId(info.ProviderIds);
            if (id is not null)
            {
                book = await _client.GetBookAsync(id.Value, cancellationToken).ConfigureAwait(false);
                queriedById = book is not null;
                if (book is null)
                {
                    _logger.LogWarning("Hardcover id {Id} on \"{Name}\" did not resolve, falling back to lookup", id, info.Name);
                }
            }

            if (book is null)
            {
                var isbn = GetIsbn(info.ProviderIds);
                if (isbn is not null)
                {
                    var isbnId = await _client.FindBookIdByIsbnAsync(isbn, cancellationToken).ConfigureAwait(false);
                    if (isbnId is not null)
                    {
                        book = await _client.GetBookAsync(isbnId.Value, cancellationToken).ConfigureAwait(false);
                        queriedById = book is not null;
                        _logger.LogDebug("Resolved ISBN {Isbn} on \"{Name}\" to Hardcover book {Id}", isbn, info.Name, isbnId);
                    }
                }
            }

            if (book is null && Config.AllowTitleSearchFallback && !string.IsNullOrWhiteSpace(info.Name))
            {
                var query = Matching.CleanForSearch(info.Name);
                var hits = await _client.SearchBooksAsync(query, SearchResultLimit, cancellationToken).ConfigureAwait(false);
                var (best, score) = Matching.Best(query, hits, info, Math.Clamp(Config.MinimumMatchScore, 0, 100));
                if (best is null)
                {
                    _logger.LogInformation("No Hardcover match for \"{Name}\" (best score {Score}, {Count} hits)", info.Name, score, hits.Count);
                    return result;
                }

                _logger.LogInformation("Matched \"{Name}\" to Hardcover book {Id} \"{Title}\" (score {Score})", info.Name, best.Id, best.Title, score);
                book = await _client.GetBookAsync(best.Id, cancellationToken).ConfigureAwait(false);
            }

            if (book is null)
            {
                return result;
            }

            result.Item = MapBook(book, info);
            result.People = MapPeople(book);
            result.HasMetadata = true;
            result.QueriedById = queriedById;
            result.Provider = Name;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Hardcover metadata for \"{Name}\"", info.Name);
        }

        return result;
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _client.GetImageAsync(url, cancellationToken);

    /// <summary>
    /// Reads the Hardcover id from a provider id dictionary.
    /// </summary>
    /// <param name="providerIds">The provider ids.</param>
    /// <returns>The id, or null.</returns>
    internal static int? GetHardcoverId(IReadOnlyDictionary<string, string>? providerIds)
    {
        if (providerIds is null)
        {
            return null;
        }

        foreach (var kv in providerIds)
        {
            if (string.Equals(kv.Key, Plugin.ProviderIdKey, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(kv.Value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                && id > 0)
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads an ISBN from a provider id dictionary (the Bookshelf OPF reader stores it under "ISBN").
    /// </summary>
    /// <param name="providerIds">The provider ids.</param>
    /// <returns>The ISBN, or null.</returns>
    internal static string? GetIsbn(IReadOnlyDictionary<string, string>? providerIds)
    {
        if (providerIds is null)
        {
            return null;
        }

        foreach (var kv in providerIds)
        {
            if (string.Equals(kv.Key, "ISBN", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kv.Value))
            {
                return kv.Value.Trim();
            }
        }

        return null;
    }

    private static RemoteSearchResult ToSearchResult(HardcoverBookSearchHit hit)
    {
        var r = new RemoteSearchResult
        {
            Name = hit.Title,
            ProductionYear = hit.ReleaseYear,
            ImageUrl = hit.ImageUrl,
            SearchProviderName = Plugin.ProviderName,
            Overview = BuildSearchOverview(hit.AuthorNames, hit.SeriesNames.FirstOrDefault(), hit.Description)
        };
        r.SetProviderId(Plugin.ProviderIdKey, hit.Id.ToString(CultureInfo.InvariantCulture));
        return r;
    }

    private static RemoteSearchResult ToSearchResult(HardcoverBook book)
    {
        var r = new RemoteSearchResult
        {
            Name = book.Title,
            ProductionYear = book.ReleaseYear,
            ImageUrl = book.Image?.Url,
            SearchProviderName = Plugin.ProviderName,
            PremiereDate = book.ReleaseDate,
            Overview = BuildSearchOverview(
                book.Contributors.Where(c => Matching.MapRole(c.Role).Kind == Jellyfin.Data.Enums.PersonKind.Author).Select(c => c.Name).ToList(),
                book.Series.FirstOrDefault()?.Name,
                book.Description)
        };
        r.SetProviderId(Plugin.ProviderIdKey, book.Id.ToString(CultureInfo.InvariantCulture));
        return r;
    }

    private static string? BuildSearchOverview(IList<string> authors, string? series, string? description)
    {
        var parts = new List<string>();
        if (authors.Count > 0)
        {
            parts.Add("By " + string.Join(", ", authors));
        }

        if (!string.IsNullOrWhiteSpace(series))
        {
            parts.Add("Series: " + series);
        }

        var header = string.Join(" · ", parts);
        if (string.IsNullOrWhiteSpace(description))
        {
            return header.Length == 0 ? null : header;
        }

        return header.Length == 0 ? description : header + "\n\n" + description;
    }

    private static Book MapBook(HardcoverBook src, BookInfo info)
    {
        var config = Config;
        var title = string.IsNullOrWhiteSpace(src.Title) ? info.Name : src.Title.Trim();
        if (config.IncludeSubtitleInTitle && !string.IsNullOrWhiteSpace(src.Subtitle))
        {
            title = title + ": " + src.Subtitle.Trim();
        }

        var book = new Book
        {
            Name = title
        };

        if (!string.IsNullOrWhiteSpace(src.Description))
        {
            book.Overview = src.Description.Trim();
        }

        if (src.ReleaseDate is DateTime date)
        {
            book.PremiereDate = date;
            book.ProductionYear = date.Year;
        }
        else if (src.ReleaseYear is int year)
        {
            book.ProductionYear = year;
        }

        if (config.ImportGenres)
        {
            IEnumerable<string> genres = src.Genres;
            if (config.MaxGenres > 0)
            {
                genres = genres.Take(config.MaxGenres);
            }

            foreach (var genre in genres)
            {
                book.AddGenre(genre);
            }
        }

        var tags = new List<string>();
        if (config.ImportMoodsAsTags)
        {
            tags.AddRange(src.Moods);
        }

        if (config.ImportContentWarningsAsTags)
        {
            tags.AddRange(src.ContentWarnings);
        }

        if (tags.Count > 0)
        {
            book.Tags = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        if (config.ImportCommunityRating && src.Rating is double rating && src.RatingsCount > 0 && rating > 0)
        {
            book.CommunityRating = (float)Math.Clamp(Math.Round(rating * 2, 1), 0, 10);
        }

        if (config.ImportSeries && src.Series.Count > 0)
        {
            var series = src.Series[0];
            book.SeriesName = series.Name;
            if (series.Position is double pos && pos > 0 && Math.Abs(pos - Math.Round(pos)) < 0.001 && pos < int.MaxValue)
            {
                book.IndexNumber = (int)Math.Round(pos);
            }
        }
        else if (!string.IsNullOrWhiteSpace(info.SeriesName))
        {
            book.SeriesName = info.SeriesName;
        }

        if (config.ImportPublisher && !string.IsNullOrWhiteSpace(src.Publisher))
        {
            book.AddStudio(src.Publisher);
        }

        if (!string.IsNullOrWhiteSpace(src.Slug))
        {
            book.HomePageUrl = "https://hardcover.app/books/" + src.Slug;
        }

        book.SetProviderId(Plugin.ProviderIdKey, src.Id.ToString(CultureInfo.InvariantCulture));
        var existingIsbn = GetIsbn(info.ProviderIds);
        var isbn = existingIsbn ?? src.Isbn13 ?? src.Isbn10;
        if (!string.IsNullOrWhiteSpace(isbn))
        {
            book.SetProviderId("ISBN", isbn);
        }

        if (!string.IsNullOrWhiteSpace(src.Asin))
        {
            book.SetProviderId("Amazon", src.Asin);
        }

        return book;
    }

    private static List<PersonInfo> MapPeople(HardcoverBook src)
    {
        var people = new List<PersonInfo>();
        if (!Config.ImportContributors)
        {
            return people;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in src.Contributors)
        {
            var (kind, role) = Matching.MapRole(c.Role);
            if (!seen.Add(c.Name + "|" + kind))
            {
                continue;
            }

            var person = new PersonInfo
            {
                Name = c.Name,
                Type = kind,
                Role = role,
                ImageUrl = c.ImageUrl
            };
            person.SetProviderId(Plugin.ProviderIdKey, c.AuthorId.ToString(CultureInfo.InvariantCulture));
            people.Add(person);
        }

        return people;
    }
}
