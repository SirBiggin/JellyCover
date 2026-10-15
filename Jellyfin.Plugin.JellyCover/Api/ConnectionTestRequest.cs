namespace Jellyfin.Plugin.JellyCover.Api;

/// <summary>
/// Request body for the connection test.
/// </summary>
public sealed class ConnectionTestRequest
{
    /// <summary>
    /// Gets or sets a token to test instead of the saved one (so the page can test before saving).
    /// </summary>
    public string? Token { get; set; }
}
