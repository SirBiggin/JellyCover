using System;
using Jellyfin.Plugin.JellyCover.Api;
using Jellyfin.Plugin.JellyCover.Providers;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyCover;

/// <summary>
/// Registers the JellyCover services with Jellyfin's dependency injection container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddTransient<HardcoverRateLimitHandler>();

        serviceCollection.AddHttpClient(HardcoverClient.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(45);
            })
            .ConfigurePrimaryHttpMessageHandler<HardcoverRateLimitHandler>()
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        serviceCollection.AddSingleton<HardcoverClient>();

        serviceCollection.AddSingleton<IRemoteMetadataProvider<Book, BookInfo>, JellyCoverBookProvider>();
        serviceCollection.AddSingleton<IRemoteMetadataProvider<Person, PersonLookupInfo>, JellyCoverPersonProvider>();
        serviceCollection.AddSingleton<IRemoteImageProvider, JellyCoverBookImageProvider>();
        serviceCollection.AddSingleton<IRemoteImageProvider, JellyCoverPersonImageProvider>();
    }
}
