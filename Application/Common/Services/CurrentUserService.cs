using System.Security.Claims;
using FloraCore.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace FloraCore.Application.Common.Services;

/// <summary>
/// Retrieves the current authenticated user's ID from the HTTP context claims.
/// </summary>
public class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

    public Guid? UserId
    {
        get
        {
            var userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return userId != null ? Guid.Parse(userId) : null;
        }
    }
}
