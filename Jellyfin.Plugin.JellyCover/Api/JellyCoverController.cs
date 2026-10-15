using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyCover.Api;

/// <summary>
/// Admin-only endpoints used by the JellyCover configuration page.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("JellyCover")]
[Produces(MediaTypeNames.Application.Json)]
public class JellyCoverController : ControllerBase
{
    private readonly HardcoverClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyCoverController"/> class.
    /// </summary>
    /// <param name="client">The Hardcover client.</param>
    public JellyCoverController(HardcoverClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Runs a real catalog search against Hardcover with the supplied (or saved) token.
    /// </summary>
    /// <param name="request">Optional token override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The test result.</returns>
    [HttpPost("Test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ConnectionTestResult>> Test([FromBody] ConnectionTestRequest? request, CancellationToken cancellationToken)
    {
        var result = await _client.TestConnectionAsync(request?.Token, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }
}
