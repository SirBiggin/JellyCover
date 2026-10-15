using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.JellyCover.Api;

/// <summary>
/// HTTP handler that keeps JellyCover inside Hardcover's published rate limits
/// (free plan: burst of 10 requests, refilling at 60 per minute) and honours
/// <c>Retry-After</c> on a 429 once before giving up.
/// </summary>
public sealed class HardcoverRateLimitHandler : DelegatingHandler
{
    private const int MaxRetryAfterSeconds = 30;

    private static readonly RateLimiter _rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 8,
        TokensPerPeriod = 1,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        AutoReplenishment = true,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 200
    });

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

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (RateLimitLease lease = await _rateLimiter.AcquireAsync(permitCount: 1, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (!lease.IsAcquired)
            {
                var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    ReasonPhrase = "JellyCover client-side rate limit queue is full"
                };
                if (lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                {
                    throttled.Headers.Add(HeaderNames.RetryAfter, retryAfter.TotalSeconds.ToString("F0", NumberFormatInfo.InvariantInfo));
                }

                _logger.LogWarning("Hardcover request queue is full, dropping request to {Url}", request.RequestUri);
                return throttled;
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

        if (delay is null || delay.Value <= TimeSpan.Zero || delay.Value.TotalSeconds > MaxRetryAfterSeconds)
        {
            _logger.LogWarning("Hardcover returned 429 Too Many Requests (Retry-After {RetryAfter}); not retrying", delay);
            return response;
        }

        _logger.LogInformation("Hardcover rate limit hit, waiting {Delay} before retrying once", delay.Value);
        response.Dispose();
        await Task.Delay(delay.Value, cancellationToken).ConfigureAwait(false);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
