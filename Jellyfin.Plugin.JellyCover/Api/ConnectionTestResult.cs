using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCover.Api;

/// <summary>
/// Result of the connection test run from the configuration page.
/// </summary>
public sealed class ConnectionTestResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the test succeeded.
    /// </summary>
    public bool Ok { get; set; }

    /// <summary>
    /// Gets or sets a human readable message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the <c>RateLimit</c> header returned by Hardcover, if any.
    /// </summary>
    public string? RateLimit { get; set; }
}
