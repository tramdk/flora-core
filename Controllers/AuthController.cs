using FloraCore.Application.Features.Auth.Commands;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Asp.Versioning;
using Microsoft.AspNetCore.Http;

namespace FloraCore.Controllers;

/// <summary>
/// Controller for authentication operations.
/// </summary>
/// <param name="mediator">The mediator instance for handling commands and queries.</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class AuthController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    /// <summary>
    /// Authenticates a user and returns tokens.
    /// </summary>
    /// <param name="command">The login credentials.</param>
    /// <returns>Auth response containing tokens.</returns>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginCommand command)
    {
        var response = await _mediator.Send(command);
        SetTokenCookies(response.AccessToken, response.RefreshToken);
        return Ok(response);
    }

    /// <summary>
    /// Registers a new user.
    /// </summary>
    /// <param name="command">The registration details.</param>
    /// <returns>Ok if successful; otherwise, BadRequest.</returns>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterCommand command)
    {
        var result = await _mediator.Send(command);
        return result ? Ok() : BadRequest(new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Registration Failed" });
    }

    /// <summary>
    /// Refreshes the access token using a refresh token.
    /// </summary>
    /// <param name="command">The refresh token command.</param>
    /// <returns>Auth response containing new tokens.</returns>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenCommand? command)
    {
        string? accessToken = command?.AccessToken ?? Request.Cookies["chinchin_token"];
        string? refreshToken = command?.RefreshToken ?? Request.Cookies["chinchin_refresh_token"];

        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
        {
            return BadRequest(new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Tokens are required for refresh." });
        }

        var response = await _mediator.Send(new RefreshTokenCommand(accessToken, refreshToken));
        SetTokenCookies(response.AccessToken, response.RefreshToken);
        return Ok(response);
    }

    /// <summary>
    /// Logs out the current user and invalidates the session.
    /// </summary>
    /// <returns>Ok if successful.</returns>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var accessToken = Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (string.IsNullOrEmpty(accessToken))
        {
            accessToken = Request.Cookies["chinchin_token"] ?? string.Empty;
        }
        await _mediator.Send(new LogoutCommand(accessToken));
        DeleteTokenCookies();
        return Ok();
    }

    private void SetTokenCookies(string accessToken, string refreshToken)
    {
        var isHttps = Request.IsHttps || Request.Headers["X-Forwarded-Proto"] == "https";

        var refreshCookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            Path = "/"
        };

        var accessCookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddMinutes(15),
            Path = "/"
        };

        Response.Cookies.Append("chinchin_token", accessToken, accessCookieOptions);
        Response.Cookies.Append("chinchin_refresh_token", refreshToken, refreshCookieOptions);
    }

    private void DeleteTokenCookies()
    {
        var isHttps = Request.IsHttps || Request.Headers["X-Forwarded-Proto"] == "https";
        
        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(-1)
        };

        Response.Cookies.Delete("chinchin_token", options);
        Response.Cookies.Delete("chinchin_refresh_token", options);
    }

    /// <summary>
    /// Changes the user's password and invalidates all existing sessions (tokens).
    /// </summary>
    /// <param name="command">The change password command.</param>
    /// <returns>Ok if successful, BadRequest if validation fails.</returns>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordCommand command)
    {
        try
        {
            var result = await _mediator.Send(command);
            return result ? Ok() : BadRequest(new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Change Password Failed" });
        }
        catch (Exception ex)
        {
            return BadRequest(new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Change Password Failed", Detail = ex.Message });
        }
    }
}
