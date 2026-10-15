using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyCover.Configuration;

/// <summary>
/// JellyCover plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the Hardcover personal access token (needs the <c>read:catalog</c> scope).
    /// </summary>
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether cover images are offered for books.
    /// </summary>
    public bool FetchBookCovers { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether author (Person) biographies and dates are fetched.
    /// </summary>
    public bool FetchAuthorMetadata { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether author photos are offered for Person items.
    /// </summary>
    public bool FetchAuthorImages { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Hardcover "Genre" tags are imported as Jellyfin genres.
    /// </summary>
    public bool ImportGenres { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of genres to import per book (0 = unlimited).
    /// </summary>
    public int MaxGenres { get; set; } = 10;

    /// <summary>
    /// Gets or sets a value indicating whether Hardcover "Mood" tags are imported as Jellyfin tags.
    /// </summary>
    public bool ImportMoodsAsTags { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Hardcover "Content Warning" tags are imported as Jellyfin tags.
    /// </summary>
    public bool ImportContentWarningsAsTags { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the Hardcover average rating (0-5) is imported as the
    /// community rating (scaled to 0-10).
    /// </summary>
    public bool ImportCommunityRating { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether series name and position are imported.
    /// </summary>
    public bool ImportSeries { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the publisher of the default edition is imported as a studio.
    /// </summary>
    public bool ImportPublisher { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether authors, illustrators, translators and other contributors
    /// are imported as people on the book.
    /// </summary>
    public bool ImportContributors { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the Hardcover subtitle is appended to the book title
    /// ("Title: Subtitle").
    /// </summary>
    public bool IncludeSubtitleInTitle { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the plugin may fall back to the best title search hit when the
    /// item has no Hardcover id or ISBN. When false only exact id / ISBN matches are used during library scans.
    /// </summary>
    public bool AllowTitleSearchFallback { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a <c>metadata.opf</c> sidecar next to the book file (Calibre writes one
    /// for every book) is read for the exact title, authors, ISBN, ASIN and series before searching Hardcover.
    /// </summary>
    public bool ReadSidecarMetadata { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum title similarity (0-100) a search hit needs before it is accepted automatically.
    /// </summary>
    public int MinimumMatchScore { get; set; } = 60;
}
