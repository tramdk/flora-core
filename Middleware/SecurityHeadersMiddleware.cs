using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace FloraCore.Middleware;

/// <summary>
/// Middleware to add security headers to HTTP responses.
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next, SecurityHeadersOptions options)
{
    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));
    private readonly SecurityHeadersOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        // 1. Prevent MIME sniffing
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");

        // 2. Prevent Clickjacking
        context.Response.Headers.Append("X-Frame-Options", _options.FrameOptions);

        // 3. Enable XSS filter
        context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");

        // 4. Control referrer information
        context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

        // 5. Content Security Policy
        if (!context.Request.Path.StartsWithSegments("/scalar"))
        {
            context.Response.Headers.Append("Content-Security-Policy", _options.ContentSecurityPolicy);
        }

        // 6. HSTS
        if (_options.IsHstsEnabled && !context.Request.Host.Host.Contains("localhost"))
        {
            context.Response.Headers.Append("Strict-Transport-Security", $"max-age={_options.HstsMaxAge}; includeSubDomains");
        }

        await _next(context);
    }
}

/// <summary>
/// Options class for configuring security headers.
/// </summary>
public class SecurityHeadersOptions
{
    /// <summary>
    /// Gets or sets the X-Frame-Options header value.
    /// </summary>
    public string FrameOptions { get; set; } = "DENY";

    /// <summary>
    /// Gets or sets the Content-Security-Policy header value.
    /// </summary>
    public string ContentSecurityPolicy { get; set; } = "default-src 'self'; script-src 'self'; style-src 'self' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com; img-src 'self' data: https:; frame-ancestors 'none';";

    /// <summary>
    /// Gets or sets a value indicating whether HSTS is enabled.
    /// </summary>
    public bool IsHstsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the HSTS max-age value (in seconds).
    /// </summary>
    public int HstsMaxAge { get; set; } = 31536000;
}

/// <summary>
/// Extension methods for adding the SecurityHeadersMiddleware to the pipeline.
/// </summary>
public static class SecurityHeadersExtensions
{
    /// <summary>
    /// Adds the SecurityHeadersMiddleware to the pipeline.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="options">The security headers options.</param>
    /// <returns>The application builder.</returns>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder, SecurityHeadersOptions options)
    {
        return builder.UseMiddleware<SecurityHeadersMiddleware>(options);
    }

    /// <summary>
    /// Adds the SecurityHeadersMiddleware to the pipeline using configuration.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="configuration">The configuration section for security headers.</param>
    /// <returns>The application builder.</returns>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder, IConfiguration configuration)
    {
        var options = configuration.GetSection("SecurityHeaders").Get<SecurityHeadersOptions>() ?? new SecurityHeadersOptions();
        return builder.UseMiddleware<SecurityHeadersMiddleware>(options);
    }

    /// <summary>
    /// Adds the SecurityHeadersMiddleware to the pipeline using the options registered in DI.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <returns>The application builder.</returns>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
    {
        var options = builder.ApplicationServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<SecurityHeadersOptions>>().Value;
        return builder.UseMiddleware<SecurityHeadersMiddleware>(options);
    }


    /// <summary>
    /// Adds Security Headers services to the Service Collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The app configuration</param>
    /// <returns>The modified service collection</returns>
    public static IServiceCollection AddSecurityHeaders(
            this IServiceCollection services,
            IConfiguration configuration)
    {
        services.Configure<SecurityHeadersOptions>(configuration.GetSection(nameof(SecurityHeadersOptions)));

        return services;
    }
}
