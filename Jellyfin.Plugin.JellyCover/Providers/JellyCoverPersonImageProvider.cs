using System;
using System.Collections.Generic;
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
/// Author photo provider for Person items, backed by Hardcover.app.
/// </summary>
public class JellyCoverPersonImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly HardcoverClient _client;
    private readonly ILogger<JellyCoverPersonImageProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyCoverPersonImageProvider"/> class.
    /// </summary>
    /// <param name="client">The Hardcover client.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{JellyCoverPersonImageProvider}"/> interface.</param>
    public JellyCoverPersonImageProvider(HardcoverClient client, ILogger<JellyCoverPersonImageProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => Plugin.ProviderName;

    /// <inheritdoc />
    public int Order => 2;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is Person;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        yield return ImageType.Primary;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var images = new List<RemoteImageInfo>();
        if (!_client.IsConfigured || Plugin.Instance?.Configuration.FetchAuthorImages == false || string.IsNullOrWhiteSpace(item.Name))
        {
            return images;
        }

        try
        {
            var author = await JellyCoverPersonProvider.ResolveAuthorAsync(_client, item.Name, item.ProviderIds, cancellationToken).ConfigureAwait(false);
            if (author?.Image is null)
            {
                return images;
            }

            images.Add(new RemoteImageInfo
            {
                ProviderName = Name,
                Type = ImageType.Primary,
                Url = author.Image.Url,
                Width = author.Image.Width,
                Height = author.Image.Height
            });
        }
        catch (HardcoverUnavailableException ex)
        {
            _logger.LogDebug("Hardcover unavailable, no photo for \"{Name}\" right now: {Reason}", item.Name, ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Hardcover author photo for \"{Name}\"", item.Name);
        }

        return images;
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _client.GetImageAsync(url, cancellationToken);
}
