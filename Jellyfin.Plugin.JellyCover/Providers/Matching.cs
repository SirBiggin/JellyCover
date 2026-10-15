using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyCover.Api;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.JellyCover.Providers;

/// <summary>
/// Title normalisation, search-hit scoring and contributor role mapping shared by the providers.
/// </summary>
internal static partial class Matching
{
    private const int PrefixMatchScore = 88;
    private const int MinPrefixLength = 12;

    private static readonly string[] _leadingArticles = { "the ", "a ", "an " };

    /// <summary>
    /// Strips file-name noise from a lookup name before it is sent to search:
    /// "(2001)" years, "(Series Book 3)" style parentheticals, "[tags]", "{tags}", Calibre's "_" for ":" and surrounding whitespace.
    /// </summary>
    /// <param name="name">The raw name.</param>
    /// <returns>The cleaned name.</returns>
    public static string CleanForSearch(string name)
    {
        var s = name.Trim();
        s = BracketedRegex().Replace(s, " ");
        s = ParentheticalRegex().Replace(s, " ");
        s = s.Replace('_', ' ');
        s = MultiSpaceRegex().Replace(s, " ").Trim();
        return s.Length == 0 ? name.Trim() : s;
    }

    /// <summary>
    /// Lower-cases, strips diacritics and punctuation, collapses whitespace, unifies a few spellings
    /// ("&amp;" / "and", "volume" / "vol") and drops a leading article.
    /// </summary>
    /// <param name="s">The text.</param>
    /// <returns>The normalised text.</returns>
    public static string Normalize(string? s) => Normalize(s, stripArticle: true);

    /// <summary>
    /// Lower-cases, strips diacritics and punctuation and collapses whitespace.
    /// </summary>
    /// <param name="s">The text.</param>
    /// <param name="stripArticle">Whether a leading "the" / "a" / "an" is dropped (not wanted for person names).</param>
    /// <returns>The normalised text.</returns>
    public static string Normalize(string? s, bool stripArticle)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return string.Empty;
        }

        var decomposed = s.Replace("&", " and ", StringComparison.Ordinal).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                sb.Append(' ');
            }
        }

        var result = MultiSpaceRegex().Replace(sb.ToString(), " ").Trim();
        result = VolumeRegex().Replace(result, "vol");
        if (stripArticle)
        {
            foreach (var article in _leadingArticles)
            {
                if (result.StartsWith(article, StringComparison.Ordinal) && result.Length > article.Length)
                {
                    result = result.Substring(article.Length);
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Similarity of two titles, 0-100.
    /// </summary>
    /// <param name="a">First title.</param>
    /// <param name="b">Second title.</param>
    /// <returns>The similarity.</returns>
    public static int TitleSimilarity(string? a, string? b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        if (na.Length == 0 || nb.Length == 0)
        {
            return 0;
        }

        if (na == nb)
        {
            return 100;
        }

        var score = 0;
        var ta = na.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var tb = nb.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var intersection = ta.Intersect(tb, StringComparer.Ordinal).Count();
        if (intersection > 0)
        {
            if (ta.IsSubsetOf(tb) || tb.IsSubsetOf(ta))
            {
                // one title fully contained in the other (e.g. "Dune" vs "Dune Messiah", or title vs title+subtitle)
                var ratio = (double)Math.Min(ta.Count, tb.Count) / Math.Max(ta.Count, tb.Count);
                score = (int)Math.Round(70 + (25 * ratio));
            }
            else
            {
                var union = ta.Union(tb, StringComparer.Ordinal).Count();
                score = (int)Math.Round(85.0 * intersection / union);
            }
        }

        // truncated file names ("Hellboy Volume 1: Seed of Destr" vs the full title)
        if (Math.Min(na.Length, nb.Length) >= MinPrefixLength
            && (na.StartsWith(nb, StringComparison.Ordinal) || nb.StartsWith(na, StringComparison.Ordinal)))
        {
            score = Math.Max(score, PrefixMatchScore);
        }

        return score;
    }

    /// <summary>
    /// How well a hit's authors agree with the authors we expect: +15 for a full-name match, +10 for a surname match,
    /// -20 when both sides are known and nothing matches, 0 when either side is unknown.
    /// </summary>
    /// <param name="hitAuthors">Author names on the search hit.</param>
    /// <param name="wanted">Author names we expect (from the OPF / file name).</param>
    /// <returns>The score adjustment.</returns>
    public static int AuthorAffinity(IList<string> hitAuthors, IReadOnlyList<string>? wanted)
    {
        if (wanted is null || wanted.Count == 0 || hitAuthors.Count == 0)
        {
            return 0;
        }

        var hits = hitAuthors.Select(h => Normalize(h, stripArticle: false)).Where(h => h.Length > 0).ToList();
        var best = 0;
        foreach (var w in wanted)
        {
            var nw = Normalize(w, stripArticle: false);
            if (nw.Length == 0)
            {
                continue;
            }

            if (hits.Contains(nw, StringComparer.Ordinal))
            {
                return 15;
            }

            var surname = nw.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (surname is not null && surname.Length >= 3
                && hits.Any(h => h.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(surname, StringComparer.Ordinal)))
            {
                best = 10;
            }
        }

        return best > 0 ? best : -20;
    }

    /// <summary>
    /// Scores a search hit against the lookup info (0-100+).
    /// </summary>
    /// <param name="query">The cleaned query title.</param>
    /// <param name="hit">The hit.</param>
    /// <param name="info">The lookup info (year / series are used as tie breakers).</param>
    /// <param name="authors">Expected authors, if known.</param>
    /// <returns>The score.</returns>
    public static int Score(string query, HardcoverBookSearchHit hit, ItemLookupInfo info, IReadOnlyList<string>? authors = null)
    {
        var score = TitleSimilarity(query, hit.Title);
        if (!string.IsNullOrWhiteSpace(hit.Subtitle))
        {
            score = Math.Max(score, TitleSimilarity(query, hit.Title + " " + hit.Subtitle));
        }

        if (info.Year is int year && hit.ReleaseYear is int hitYear)
        {
            if (year == hitYear)
            {
                score += 10;
            }
            else if (Math.Abs(year - hitYear) > 2 && score < 100)
            {
                score -= 5;
            }
        }

        if (info is BookInfo book && !string.IsNullOrWhiteSpace(book.SeriesName)
            && hit.SeriesNames.Any(s => Normalize(s) == Normalize(book.SeriesName)))
        {
            score += 10;
        }

        score += AuthorAffinity(hit.AuthorNames, authors);
        return score;
    }

    /// <summary>
    /// Picks the best hit, or null if nothing reaches <paramref name="minimumScore"/>.
    /// </summary>
    /// <param name="query">The cleaned query title.</param>
    /// <param name="hits">The hits in relevance order.</param>
    /// <param name="info">The lookup info.</param>
    /// <param name="minimumScore">Minimum acceptable score.</param>
    /// <param name="authors">Expected authors, if known.</param>
    /// <returns>The best hit and its score.</returns>
    public static (HardcoverBookSearchHit? Hit, int Score) Best(string query, IReadOnlyList<HardcoverBookSearchHit> hits, ItemLookupInfo info, int minimumScore, IReadOnlyList<string>? authors = null)
    {
        HardcoverBookSearchHit? best = null;
        var bestScore = int.MinValue;
        for (var i = 0; i < hits.Count; i++)
        {
            // small relevance bonus so ties go to Hardcover's own ranking
            var s = Score(query, hits[i], info, authors) - i;
            if (s > bestScore)
            {
                best = hits[i];
                bestScore = s;
            }
        }

        return best is not null && bestScore >= minimumScore ? (best, bestScore) : (null, bestScore);
    }

    /// <summary>
    /// Maps a Hardcover contribution label to a Jellyfin person kind.
    /// </summary>
    /// <param name="role">The Hardcover contribution (null / empty = primary author).</param>
    /// <returns>The person kind and the role label to store.</returns>
    public static (PersonKind Kind, string? Role) MapRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return (PersonKind.Author, null);
        }

        var r = role.Trim();
        var l = r.ToLowerInvariant();
        if (l == "author" || l == "writer" || l == "creator")
        {
            return (PersonKind.Author, null);
        }

        if (l.Contains("illustrat", StringComparison.Ordinal))
        {
            return (PersonKind.Illustrator, r);
        }

        if (l.Contains("translat", StringComparison.Ordinal))
        {
            return (PersonKind.Translator, r);
        }

        if (l.Contains("narrat", StringComparison.Ordinal) || l.Contains("reader", StringComparison.Ordinal))
        {
            return (PersonKind.Narrator, r);
        }

        if (l.Contains("editor", StringComparison.Ordinal))
        {
            return (PersonKind.Editor, r);
        }

        if (l.Contains("cover", StringComparison.Ordinal))
        {
            return (PersonKind.CoverArtist, r);
        }

        if (l.Contains("color", StringComparison.Ordinal) || l.Contains("colour", StringComparison.Ordinal))
        {
            return (PersonKind.Colorist, r);
        }

        if (l.Contains("letter", StringComparison.Ordinal))
        {
            return (PersonKind.Letterer, r);
        }

        if (l.Contains("pencil", StringComparison.Ordinal))
        {
            return (PersonKind.Penciller, r);
        }

        if (l.Contains("ink", StringComparison.Ordinal))
        {
            return (PersonKind.Inker, r);
        }

        if (l.Contains("artist", StringComparison.Ordinal) || l.Contains("art", StringComparison.Ordinal))
        {
            return (PersonKind.Artist, r);
        }

        // foreword, afterword, introduction, contributor, ghostwriter...
        return (PersonKind.Writer, r);
    }

    [GeneratedRegex(@"[\[\{][^\]\}]*[\]\}]")]
    private static partial Regex BracketedRegex();

    [GeneratedRegex(@"\s*\([^()]*\)")]
    private static partial Regex ParentheticalRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"\bvolume\b")]
    private static partial Regex VolumeRegex();
}
