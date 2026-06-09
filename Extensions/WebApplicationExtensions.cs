using System.Text.Json;
using AspNetCoreRateLimit;
using FloraCore.Application.Common.Constants;
using FloraCore.Middleware;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;
using Serilog;

namespace FloraCore.Extensions;

/// <summary>
/// Extension methods for WebApplication to organize middleware pipeline and endpoint routing.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Configure the HTTP request pipeline with middlewares.
    /// </summary>
    public static WebApplication UseAppMiddlewares(this WebApplication app)
    {
        // 1. Exception handling phải đứng đầu tiên để bắt mọi exception từ tất cả middleware bên dưới
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        // 2. Response Compression đứng sớm, trước StaticFiles để nén được cả file tĩnh
        app.UseResponseCompression();
        // 3. Rate Limiting đứng ngay sau exception handler, bảo vệ toàn bộ pipeline kể cả static files
        if (!app.Environment.IsEnvironment("Testing"))
        {
            app.UseIpRateLimiting();
        }
        // 4. Security Headers gắn vào mọi response, bao gồm cả file tĩnh
        app.UseSecurityHeaders();
        // 5. Serilog logging sớm để ghi log đầy đủ cho tất cả request
        app.UseSerilogRequestLogging();
        // 6. Static files (phải sau Compression và SecurityHeaders)
        app.UseDefaultFiles();
        app.UseStaticFiles();
        // 7. Routing
        app.UseRouting();
        // 8. CORS phải sau UseRouting và trước UseAuthentication (yêu cầu của ASP.NET Core)
        app.UseCors(CorsConstants.AllowFrontend);

        // Custom Health Checks Endpoint
        app.UseHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                var response = new
                {
                    status = report.Status.ToString(),
                    checks = report.Entries.Select(entry => new
                    {
                        name = entry.Key,
                        status = entry.Value.Status.ToString(),
                        description = entry.Value.Description,
                        duration = entry.Value.Duration
                    }),
                    totalDuration = report.TotalDuration
                };
                await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
            }
        });

        // Scalar API Documentation
        app.UseSwagger(options => options.RouteTemplate = "openapi/{documentName}.json");
        app.MapScalarApiReference(options =>
        {
            options.WithTitle("Flora Core API v1")
                   .WithTheme(ScalarTheme.Moon)
                   .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
        });

        app.UseAuthentication();
        app.UseMiddleware<TokenBlacklistMiddleware>();
        app.UseAuthorization();

        return app;
    }

    /// <summary>
    /// Map Web API endpoints, SignalR hubs, and Hangfire dashboard.
    /// </summary>
    public static WebApplication MapAppEndpoints(this WebApplication app)
    {
        app.MapControllers();

        // Hangfire Dashboard (Restricted to authenticated Admin users)
        if (!app.Environment.IsEnvironment("Testing"))
        {
            app.MapHangfireDashboard("/hangfire", new DashboardOptions
            {
                Authorization = [new FloraCore.Infrastructure.Security.HangfireDashboardAuthFilter()]
            });

            // Register Hangfire Recurring Jobs
            var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
            jobManager.AddOrUpdate<FloraCore.Infrastructure.Services.OutboxProcessor>(
                "outbox-processor",
                processor => processor.ProcessMessagesAsync(),
                Cron.Minutely());
        }

        // SignalR Hubs
        app.MapHub<FloraCore.Infrastructure.Hubs.ChatHub>("/hubs/chat");
        app.MapHub<FloraCore.Infrastructure.Hubs.NotificationHub>("/hubs/notifications");

        return app;
    }
}
