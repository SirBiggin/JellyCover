using System;

namespace Jellyfin.Plugin.JellyCover.Api;

/// <summary>
/// Thrown when Hardcover could not be used for a transient reason: the rate limit is in effect,
/// the API answered 5xx, the request timed out or the network failed. Providers catch it and leave the
/// item untouched for a later refresh instead of falling back to weaker lookups.
/// </summary>
public class HardcoverUnavailableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HardcoverUnavailableException"/> class.
    /// </summary>
    public HardcoverUnavailableException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HardcoverUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The reason.</param>
    public HardcoverUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HardcoverUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The reason.</param>
    /// <param name="innerException">The underlying exception.</param>
    public HardcoverUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
