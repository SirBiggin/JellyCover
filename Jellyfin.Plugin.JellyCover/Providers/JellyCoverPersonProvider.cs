using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCover.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCover.Providers;

/// <summary>
/// Author (Person) metadata provider backed by Hardcover.app: biography, birth / death dates, location, photo.
/// </summary>
public class JellyCoverPersonProvider : IRemoteMetadataProvider<Person, PersonLookupInfo>
{
    private readonly HardcoverClient _client;
    private readonly ILogger<JellyCoverPersonProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyCoverPersonProvider"/> class.
    /// </summary>
    /// <param name="client">The Hardcover client.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{JellyCoverPersonProvider}"/> interface.</param>
    public JellyCoverPersonProvider(HardcoverClient client, ILogger<JellyCoverPersonProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => Plugin.ProviderName;

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(PersonLookupInfo searchInfo, CancellationToken cancellationToken)
    {
        var results = new List<RemoteSearchResult>();
        if (!_client.IsConfigured || string.IsNullOrWhiteSpace(searchInfo.Name))
        {
            return results;
        }

        var id = JellyCoverBookProvider.GetHardcoverId(searchInfo.ProviderIds);
        if (id is not null)
        {
            var author = await _client.GetAuthorAsync(id.Value, cancellationToken).ConfigureAwait(false);
            if (author is not null)
            {
                results.Add(ToSearchResult(author));
                return results;
            }
        }

        var hits = await _client.SearchAuthorsAsync(searchInfo.Name, 8, cancellationToken).ConfigureAwait(false);
        foreach (var hit in hits)
        {
            var r = new RemoteSearchResult
            {
                Name = hit.Name,
                ImageUrl = hit.ImageUrl,
                SearchProviderName = Plugin.ProviderName,
                Overview = DescribeHit(hit)
            };
            r.SetProviderId(Plugin.ProviderIdKey, hit.Id.ToString(CultureInfo.InvariantCulture));
            results.Add(r);
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<MetadataResult<Person>> GetMetadata(PersonLookupInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Person>();
        if (!_client.IsConfigured || Plugin.Instance?.Configuration.FetchAuthorMetadata == false || string.IsNullOrWhiteSpace(info.Name))
        {
            return result;
        }

        try
        {
            var author = await ResolveAuthorAsync(_client, info.Name, info.ProviderIds, cancellationToken).ConfigureAwait(false);
            if (author is null)
            {
                _logger.LogDebug("No exact Hardcover author match for \"{Name}\"", info.Name);
                return result;
            }

            var person = new Person
            {
                // keep Jellyfin's name so the person record is not renamed / duplicated
                Name = info.Name
            };

            if (!string.IsNullOrWhiteSpace(author.Bio))
            {
                person.Overview = author.Bio.Trim();
            }

            if (author.BornDate is DateTime born)
            {
                person.PremiereDate = born;
                person.ProductionYear = born.Year;
            }

            if (author.DeathDate is DateTime died)
            {
                person.EndDate = died;
            }

            if (!string.IsNullOrWhiteSpace(author.Location))
            {
                person.ProductionLocations = [author.Location.Trim()];
            }

            if (!string.IsNullOrWhiteSpace(author.Slug))
            {
                person.HomePageUrl = "https://hardcover.app/authors/" + author.Slug;
            }

            person.SetProviderId(Plugin.ProviderIdKey, author.Id.ToString(CultureInfo.InvariantCulture));

            result.Item = person;
            result.HasMetadata = true;
            result.Provider = Name;
            result.QueriedById = JellyCoverBookProvider.GetHardcoverId(info.ProviderIds) is not null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Hardcover author metadata for \"{Name}\"", info.Name);
        }

        return result;
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _client.GetImageAsync(url, cancellationToken);

    /// <summary>
    /// Resolves a person to a Hardcover author: by stored id, otherwise by an exact (normalised) name match
    /// among the top search hits. Loose matches are deliberately rejected because Person items are shared
    /// with movie / TV people.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="name">The person name.</param>
    /// <param name="providerIds">The person's provider ids.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The author, or null.</returns>
    internal static async Task<HardcoverAuthor?> ResolveAuthorAsync(HardcoverClient client, string name, IReadOnlyDictionary<string, string>? providerIds, CancellationToken cancellationToken)
    {
        var id = JellyCoverBookProvider.GetHardcoverId(providerIds);
        if (id is not null)
        {
            var byId = await client.GetAuthorAsync(id.Value, cancellationToken).ConfigureAwait(false);
            if (byId is not null)
            {
                return byId;
            }
        }

        var wanted = Matching.Normalize(name);
        if (wanted.Length == 0)
        {
            return null;
        }

        var hits = await client.SearchAuthorsAsync(name, 8, cancellationToken).ConfigureAwait(false);
        var match = hits.FirstOrDefault(h => Matching.Normalize(h.Name) == wanted)
                    ?? hits.FirstOrDefault(h => h.AlternateNames.Any(a => Matching.Normalize(a) == wanted));
        if (match is null)
        {
            return null;
        }

        return await client.GetAuthorAsync(match.Id, cancellationToken).ConfigureAwait(false);
    }

    private static RemoteSearchResult ToSearchResult(HardcoverAuthor author)
    {
        var r = new RemoteSearchResult
        {
            Name = author.Name,
            ImageUrl = author.Image?.Url,
            SearchProviderName = Plugin.ProviderName,
            Overview = author.Bio,
            PremiereDate = author.BornDate
        };
        r.SetProviderId(Plugin.ProviderIdKey, author.Id.ToString(CultureInfo.InvariantCulture));
        return r;
    }

    private static string? DescribeHit(HardcoverAuthorSearchHit hit)
    {
        var parts = new List<string>();
        if (hit.BooksCount > 0)
        {
            parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} books", hit.BooksCount));
        }

        if (hit.Books.Count > 0)
        {
            parts.Add(string.Join(", ", hit.Books.Take(5)));
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}
