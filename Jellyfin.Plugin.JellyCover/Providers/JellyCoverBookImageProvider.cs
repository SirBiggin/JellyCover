using System;
using System.Collections.Generic;
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
/// Cover image provider for books, backed by Hardcover.app.
/// </summary>
public class JellyCoverBookImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly HardcoverClient _client;
    private readonly ILogger<JellyCoverBookImageProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyCoverBookImageProvider"/> class.
    /// </summary>
    /// <param name="client">The Hardcover client.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{JellyCoverBookImageProvider}"/> interface.</param>
    public JellyCoverBookImageProvider(HardcoverClient client, ILogger<JellyCoverBookImageProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => Plugin.ProviderName;

    /// <inheritdoc />
    public int Order => 1;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is Book;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        yield return ImageType.Primary;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var images = new List<RemoteImageInfo>();
        if (!_client.IsConfigured || Plugin.Instance?.Configuration.FetchBookCovers == false)
        {
            return images;
        }

        try
        {
            var id = JellyCoverBookProvider.GetHardcoverId(item.ProviderIds);

            if (id is null)
            {
                var isbn = JellyCoverBookProvider.GetIsbn(item.ProviderIds);
                if (isbn is not null)
                {
                    id = await _client.FindBookIdByIsbnAsync(isbn, cancellationToken).ConfigureAwait(false);
                }
            }

            if (id is null && !string.IsNullOrWhiteSpace(item.Name) && Plugin.Instance?.Configuration.AllowTitleSearchFallback != false)
            {
                var query = Matching.CleanForSearch(item.Name);
                var hits = await _client.SearchBooksAsync(query, 5, cancellationToken).ConfigureAwait(false);
                var info = new BookInfo { Name = item.Name, Year = item.ProductionYear };
                if (item is Book b && !string.IsNullOrWhiteSpace(b.SeriesName))
                {
                    info.SeriesName = b.SeriesName;
                }

                var minimum = Math.Clamp(Plugin.Instance?.Configuration.MinimumMatchScore ?? 60, 0, 100);
                var (best, _) = Matching.Best(query, hits, info, minimum);
                id = best?.Id;
            }

            if (id is null)
            {
                _logger.LogDebug("No Hardcover book id for \"{Name}\", no covers offered", item.Name);
                return images;
            }

            var covers = await _client.GetBookCoversAsync(id.Value, cancellationToken).ConfigureAwait(false);
            images.AddRange(covers.Select(c => new RemoteImageInfo
            {
                ProviderName = Name,
                Type = ImageType.Primary,
                Url = c.Url,
                Width = c.Width,
                Height = c.Height
            }));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Hardcover covers for \"{Name}\"", item.Name);
        }

        return images;
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _client.GetImageAsync(url, cancellationToken);
}
