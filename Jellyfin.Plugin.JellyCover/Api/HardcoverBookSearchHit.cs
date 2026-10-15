using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCover.Api;

/// <summary>
/// A hit from the Hardcover book search (Typesense document).
/// </summary>
public sealed class HardcoverBookSearchHit
{
    /// <summary>
    /// Gets or sets the Hardcover book id.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the subtitle.
    /// </summary>
    public string? Subtitle { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the cover image url.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Gets or sets the release year.
    /// </summary>
    public int? ReleaseYear { get; set; }

    /// <summary>
    /// Gets or sets the url slug.
    /// </summary>
    public string? Slug { get; set; }

    /// <summary>
    /// Gets or sets the average rating (0-5).
    /// </summary>
    public double? Rating { get; set; }

    /// <summary>
    /// Gets or sets the number of Hardcover users who shelved the book.
    /// </summary>
    public int UsersCount { get; set; }

    /// <summary>
    /// Gets the author names.
    /// </summary>
    public IList<string> AuthorNames { get; } = new List<string>();

    /// <summary>
    /// Gets the series names.
    /// </summary>
    public IList<string> SeriesNames { get; } = new List<string>();

    /// <summary>
    /// Gets the ISBNs.
    /// </summary>
    public IList<string> Isbns { get; } = new List<string>();
}
