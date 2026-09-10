using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Seniorfestival.Api;

/// <summary>The parts of the admin site access is handed out for.</summary>
public static class AdminArea
{
    public const string Votings = "votings";
    public const string Activities = "activities";
    public const string Settings = "settings";

    public static readonly string[] All = [Votings, Activities, Settings];
}

/// <summary>
/// Password gate for the admin endpoints. Access is per area, so one person can be given
/// the activities but not the votings.
/// <para>
/// Configuration is one app setting per area - <c>adminPassword_votings</c>,
/// <c>adminPassword_activities</c>, <c>adminPassword_settings</c> - each holding the
/// passwords that open it, comma separated. A password that appears in two settings opens
/// both, which is how someone who needs two areas still only gets one password to
/// remember. <c>adminPassword</c> without a suffix opens everything.
/// </para>
/// <para>
/// This is the admin site's real credential, not its function keys: those are compiled
/// into the JavaScript bundle and can be read by anyone who can download the page, so
/// they gate nothing on their own. Passwords never reach the bundle - one is typed in,
/// kept in the tab, and sent on every request.
/// </para>
/// <para>
/// There are no accounts, no roles beyond these areas and no rate limiting, so a password
/// needs to be long enough that guessing is hopeless. It travels in a header rather than
/// the query string because request URLs end up in the host's logs.
/// </para>
/// </summary>
public static class AdminAuth
{
    public const string HeaderName = "X-Admin-Password";

    private const string FullAccessSetting = "adminPassword";

    /// <summary>
    /// Null when the request may go ahead; otherwise the result to return instead.
    /// 401 means the password is not recognised at all, 403 that it is - but not for this
    /// area. The admin site tells them apart: the first sends you back to the login
    /// screen, the second does not.
    /// </summary>
    public static IActionResult? Check(HttpRequest req, ILogger logger, string area)
    {
        if (!IsConfigured())
        {
            // Fails closed on purpose. A missing setting has to lock the door rather than
            // remove it, or a fat-fingered deployment would quietly publish the admin API.
            logger.LogError(
                "No admin password settings are configured - refusing every admin request. "
                + "Set {Setting}, or one of the per-area settings.",
                FullAccessSetting);

            return new ObjectResult(new { error = "Adminadgang er ikke sat op på serveren." })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
        }

        var granted = GrantedAreas(req.Headers[HeaderName].ToString());

        if (granted.Length == 0)
        {
            return new UnauthorizedObjectResult(new { error = "Forkert adgangskode." });
        }

        if (!granted.Contains(area))
        {
            return new ObjectResult(new { error = "Adgangskoden giver ikke adgang til denne del." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        return null;
    }

    /// <summary>
    /// The areas <paramref name="password"/> opens, in <see cref="AdminArea.All"/> order.
    /// Empty for a password nobody has been given.
    /// </summary>
    public static string[] GrantedAreas(string password)
    {
        if (password.Length == 0)
        {
            return [];
        }

        return AdminArea.All.Where(area => Opens(password, area)).ToArray();
    }

    public static bool IsConfigured() => AdminArea.All.Any(area => AcceptedFor(area).Length > 0);

    private static bool Opens(string password, string area)
    {
        bool matched = false;

        // Every candidate is compared, and |= does not short-circuit, so the time taken
        // does not reveal which entry matched or how far down the list it sat.
        foreach (string accepted in AcceptedFor(area))
        {
            matched |= MatchesExactly(password, accepted);
        }

        return matched;
    }

    private static string[] AcceptedFor(string area)
    {
        return Split(Environment.GetEnvironmentVariable(FullAccessSetting))
            .Concat(Split(Environment.GetEnvironmentVariable($"{FullAccessSetting}_{area}")))
            .ToArray();
    }

    private static string[] Split(string? setting) =>
        (setting ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Compares in constant time for equal-length input, so the comparison itself cannot
    /// be timed to recover a password one character at a time. Unequal lengths return
    /// false immediately, which gives away only the length.
    /// </summary>
    private static bool MatchesExactly(string provided, string expected)
    {
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(expected));
    }
}
