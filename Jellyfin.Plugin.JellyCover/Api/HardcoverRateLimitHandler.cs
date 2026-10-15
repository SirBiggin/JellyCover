using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCover.Api;

/// <summary>
/// HTTP handler that keeps JellyCover inside Hardcover's published rate limits
/// (free plan: burst of 10 requests, 60 per minute, 5,000 per day).
/// Short <c>Retry-After</c> values on a 429 are honoured once; a long one (the daily cap)
/// pauses every JellyCover request until it expires, so a library scan that runs into the cap
/// stops hammering the API and stops filling the log.
/// </summary>
public sealed class HardcoverRateLimitHandler : DelegatingHandler
{
    private const int MaxRetryAfterSeconds = 30;

    private static readonly TimeSpan _maxPause = TimeSpan.FromHours(24);
    private static readonly TimeSpan _pauseLogInterval = TimeSpan.FromMinutes(10);

    private static readonly RateLimiter _rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 8,
        TokensPerPeriod = 1,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        AutoReplenishment = true,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 200
    });

    // UTC ticks; 0 = not paused. Static so every HttpClient instance the factory hands out shares the pause.
    private static long _pausedUntilTicks;
    private static long _lastPauseLogTicks;

    private readonly ILogger<HardcoverRateLimitHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HardcoverRateLimitHandler"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{HardcoverRateLimitHandler}"/> interface.</param>
    public HardcoverRateLimitHandler(ILogger<HardcoverRateLimitHandler> logger)
        : base(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            RequestHeaderEncodingSelector = (_, _) => Encoding.UTF8
        })
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the moment the current rate-limit pause ends, or null when requests are allowed.
    /// </summary>
    public static DateTimeOffset? PausedUntil
    {
        get
        {
            var ticks = Interlocked.Read(ref _pausedUntilTicks);
            if (ticks == 0)
            {
                return null;
            }

            var until = new DateTimeOffset(ticks, TimeSpan.Zero);
            return until > DateTimeOffset.UtcNow ? until : null;
        }
    }

    /// <summary>
    /// Clears a pause (used by the connection test so a fresh token can be tried immediately).
    /// </summary>
    public static void ClearPause() => Interlocked.Exchange(ref _pausedUntilTicks, 0);

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (PausedUntil is DateTimeOffset pausedUntil)
        {
            var remaining = pausedUntil - DateTimeOffset.UtcNow;
            var now = DateTimeOffset.UtcNow.UtcTicks;
            var last = Interlocked.Read(ref _lastPauseLogTicks);
            if (now - last > _pauseLogInterval.Ticks && Interlocked.CompareExchange(ref _lastPauseLogTicks, now, last) == last)
            {
                _logger.LogInformation(
                    "Hardcover rate limit still in effect; JellyCover is skipping requests for another {Remaining} (until {Until:yyyy-MM-dd HH:mm} local)",
                    Round(remaining),
                    pausedUntil.ToLocalTime());
            }

            return Synthetic429(request, remaining, "JellyCover paused: Hardcover rate limit in effect");
        }

        using (RateLimitLease lease = await _rateLimiter.AcquireAsync(permitCount: 1, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (!lease.IsAcquired)
            {
                var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan queueDelay) ? queueDelay : TimeSpan.FromSeconds(5);
                _logger.LogWarning("Hardcover request queue is full, dropping request to {Url}", request.RequestUri);
                return Synthetic429(request, retryAfter, "JellyCover client-side rate limit queue is full");
            }
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.TooManyRequests)
        {
            return response;
        }

        var delay = response.Headers.RetryAfter?.Delta;
        if (delay is null && response.Headers.RetryAfter?.Date is DateTimeOffset date)
        {
            delay = date - DateTimeOffset.UtcNow;
        }

        if (delay is null || delay.Value <= TimeSpan.Zero)
        {
            _logger.LogWarning("Hardcover returned 429 Too Many Requests without a usable Retry-After; not retrying");
            return response;
        }

        if (delay.Value.TotalSeconds > MaxRetryAfterSeconds)
        {
            Pause(delay.Value);
            return response;
        }

        _logger.LogInformation("Hardcover rate limit hit, waiting {Delay} before retrying once", delay.Value);
        response.Dispose();
        await Task.Delay(delay.Value, cancellationToken).ConfigureAwait(false);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static TimeSpan Round(TimeSpan t) => TimeSpan.FromMinutes(Math.Ceiling(t.TotalMinutes));

    private static HttpResponseMessage Synthetic429(HttpRequestMessage request, TimeSpan retryAfter, string reason)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            ReasonPhrase = reason,
            RequestMessage = request,
            Content = new StringContent(string.Empty)
        };
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter > TimeSpan.Zero ? retryAfter : TimeSpan.FromSeconds(1));
        return response;
    }

    private void Pause(TimeSpan delay)
    {
        var pause = delay > _maxPause ? _maxPause : delay;
        var now = DateTimeOffset.UtcNow;
        var until = now + pause;
        var previous = Interlocked.Exchange(ref _pausedUntilTicks, until.UtcTicks);
        Interlocked.Exchange(ref _lastPauseLogTicks, now.UtcTicks);

        // several in-flight requests can all come back 429 at once; warn only for the one that started the pause
        if (previous < now.UtcTicks)
        {
            _logger.LogWarning(
                "Hardcover rate limit exceeded (Retry-After {Delay}); pausing all JellyCover requests until {Until:yyyy-MM-dd HH:mm} local. Items skipped meanwhile are left untouched and picked up by a later refresh. The free plan allows 5,000 requests per day.",
                Round(delay),
                until.ToLocalTime());
        }
    }
}
