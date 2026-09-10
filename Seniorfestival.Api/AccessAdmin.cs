using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Seniorfestival.Api;

/// <summary>
/// Says which parts of the admin site a password opens. The admin site calls this when
/// someone logs in, and renders only the pages it names.
/// <para>
/// The answer is advisory - every other endpoint checks the password again for its own
/// area, so a hidden page is not the same as a protected one.
/// </para>
/// Named AccessAdmin rather than AdminAccess because the Functions host reserves every
/// route starting with "admin" and refuses to load the function.
/// </summary>
public class AccessAdmin
{
    private readonly ILogger<AccessAdmin> _logger;

    public AccessAdmin(ILogger<AccessAdmin> logger)
    {
        _logger = logger;
    }

    [Function("AccessAdmin")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequest req)
    {
        if (!AdminAuth.IsConfigured())
        {
            _logger.LogError("No admin password settings are configured - refusing the access lookup.");

            return new ObjectResult(new { error = "Adminadgang er ikke sat op på serveren." })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
        }

        var areas = AdminAuth.GrantedAreas(req.Headers[AdminAuth.HeaderName].ToString());

        if (areas.Length == 0)
        {
            return new UnauthorizedObjectResult(new { error = "Forkert adgangskode." });
        }

        return new OkObjectResult(new { areas });
    }
}
