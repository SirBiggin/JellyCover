using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCover.Api;

/// <summary>
/// Full book details from Hardcover.
/// </summary>
public sealed class HardcoverBook
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
    /// Gets or sets the original release date.
    /// </summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets the release year.
    /// </summary>
    public int? ReleaseYear { get; set; }

    /// <summary>
    /// Gets or sets the page count.
    /// </summary>
    public int? Pages { get; set; }

    /// <summary>
    /// Gets or sets the average rating (0-5).
    /// </summary>
    public double? Rating { get; set; }

    /// <summary>
    /// Gets or sets the number of ratings.
    /// </summary>
    public int RatingsCount { get; set; }

    /// <summary>
    /// Gets or sets the url slug.
    /// </summary>
    public string? Slug { get; set; }

    /// <summary>
    /// Gets or sets the literary type (1 = fiction, 2 = nonfiction).
    /// </summary>
    public int? LiteraryTypeId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the book is a compilation.
    /// </summary>
    public bool Compilation { get; set; }

    /// <summary>
    /// Gets or sets the cover image.
    /// </summary>
    public HardcoverImage? Image { get; set; }

    /// <summary>
    /// Gets or sets the publisher of the default edition.
    /// </summary>
    public string? Publisher { get; set; }

    /// <summary>
    /// Gets or sets the ISBN-13 of the default edition.
    /// </summary>
    public string? Isbn13 { get; set; }

    /// <summary>
    /// Gets or sets the ISBN-10 of the default edition.
    /// </summary>
    public string? Isbn10 { get; set; }

    /// <summary>
    /// Gets or sets the ASIN of the default edition.
    /// </summary>
    public string? Asin { get; set; }

    /// <summary>
    /// Gets the genre tags.
    /// </summary>
    public IList<string> Genres { get; } = new List<string>();

    /// <summary>
    /// Gets the mood tags.
    /// </summary>
    public IList<string> Moods { get; } = new List<string>();

    /// <summary>
    /// Gets the content warning tags.
    /// </summary>
    public IList<string> ContentWarnings { get; } = new List<string>();

    /// <summary>
    /// Gets the contributors.
    /// </summary>
    public IList<HardcoverContributor> Contributors { get; } = new List<HardcoverContributor>();

    /// <summary>
    /// Gets the series memberships (featured first).
    /// </summary>
    public IList<HardcoverSeriesEntry> Series { get; } = new List<HardcoverSeriesEntry>();
}
