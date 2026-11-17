using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ItsmNc.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize]
public sealed class AuthController : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<CurrentUserResponse> Me()
    {
        var roles = User.FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new CurrentUserResponse(
            User.FindFirstValue("sub")!,
            User.FindFirstValue("preferred_username"),
            roles,
            User.FindFirstValue("team_slug"));
    }
}

public sealed record CurrentUserResponse(
    string Subject,
    string? UserName,
    IReadOnlyList<string> Roles,
    string? TeamSlug);
