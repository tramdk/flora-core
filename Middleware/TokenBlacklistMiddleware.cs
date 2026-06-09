using System.IdentityModel.Tokens.Jwt;
using FloraCore.Application.Common.Services;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;

namespace FloraCore.Middleware;

/// <summary>
/// Middleware to check if a token is blacklisted.
/// </summary>
public class TokenBlacklistMiddleware(RequestDelegate next, ILogger<TokenBlacklistMiddleware> logger)
{
    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));
    private readonly ILogger<TokenBlacklistMiddleware> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="blacklistService">The token blacklist service.</param>
    public async Task InvokeAsync(HttpContext context, ITokenBlacklistService blacklistService)
    {
        var token = context.Request.Headers.Authorization.ToString().Replace("Bearer ", "");

        if (!string.IsNullOrEmpty(token))
        {
            var handler = new JwtSecurityTokenHandler();
            try
            {
                if (handler.CanReadToken(token))
                {
                    var jwtToken = handler.ReadJwtToken(token);
                    var jti = jwtToken.Id;

                    if (await blacklistService.IsBlacklistedAsync(jti))
                    {
                        _logger.LogWarning("Blacklisted token used. JTI: {Jti}", jti);
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsync("Token is blacklisted");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading or validating token.");
            }
        }

        await _next(context);
    }
}
