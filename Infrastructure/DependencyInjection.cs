using FloraCore.Application.Interfaces;
using FloraCore.Application.Common.Interfaces;
using FloraCore.Application.Common.Services;
using FloraCore.Application.Common.Models;
using FloraCore.Domain.Entities;
using FloraCore.Infrastructure.Data;
using FloraCore.Infrastructure.Repositories;
using FloraCore.Infrastructure.Services;
using FloraCore.Infrastructure.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Hangfire;
using Hangfire.PostgreSql;
using OpenTelemetry.Resources;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Http.Resilience;
using Polly.Registry;

namespace FloraCore.Infrastructure;

/// <summary>
/// Extension methods for IServiceCollection to register infrastructure services.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds core infrastructure services including database, identity, repositories, and services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The modified service collection.</returns>
    /// <exception cref="ArgumentNullException">Thrown if services or configuration is null.</exception>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Database
        var databaseProvider = configuration["DatabaseProvider"] ?? "SqlServer";
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        services.AddDbContext<AppDbContext>(options =>
        {
            if (databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            {
                options.UseNpgsql(connectionString, b => b.MigrationsAssembly("FloraCore"));
            }
            else
            {
                options.UseSqlServer(connectionString, b => b.MigrationsAssembly("FloraCore"));
            }

            options.ConfigureWarnings(warnings =>
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        });

        services.AddIdentity<AppUser, IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        // Repositories
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IGenericRepository<,>), typeof(GenericRepository<,>));
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IWebsiteInfoRepository, WebsiteInfoRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();

        // Register IPostQueryDialect strategy dynamically based on DatabaseProvider
        if (databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IPostQueryDialect, PostgresPostQueryDialect>();
        }
        else if (databaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IPostQueryDialect, SqlServerPostQueryDialect>();
        }
        else
        {
            services.AddSingleton<IPostQueryDialect, SqlitePostQueryDialect>();
        }

        services.AddScoped<IPostQueryService, PostQueryService>();

        // Services
        services.AddSingleton<IDateTimeService, DateTimeService>();
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<ITokenBlacklistService, TokenBlacklistService>();

#pragma warning disable EXTEXP0018
        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5)
            };
        });
#pragma warning restore EXTEXP0018

        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IAdminNotificationService, AdminNotificationService>();
        services.AddScoped<OutboxProcessor>();
        services.AddScoped<IInboxService, InboxService>();
        services.AddScoped<IEmailService, EmailService>();

        // Crawler Service Registration
        services.Configure<FanpageAiManagerSettings>(configuration.GetSection("FanpageAiManagerSettings"));
        services.AddHttpClient<IPostCrawlerService, PostCrawlerService>();

        // Configure JwtSettings
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));

        // Configure MailSettings
        services.Configure<MailSettings>(configuration.GetSection(MailSettings.SectionName));

        // Configure Cloudinary
        services.Configure<CloudinarySettings>(configuration.GetSection("Cloudinary"));

        // Configure PaymentGateways
        services.Configure<PaymentGatewaysOptions>(configuration.GetSection(PaymentGatewaysOptions.SectionName));

        // Register CloudinaryFileService with IOptions
        services.AddScoped<IFileService, CloudinaryFileService>(sp =>
        {
            var repository = sp.GetRequiredService<IGenericRepository<FileMetadata, Guid>>();
            var config = sp.GetRequiredService<IOptions<CloudinarySettings>>();
            var currentUserService = sp.GetRequiredService<ICurrentUserService>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var pipelineProvider = sp.GetRequiredService<ResiliencePipelineProvider<string>>();

            var resourceManager = sp.GetRequiredService<IResourceManager>();
            return new CloudinaryFileService(
                repository,
                config,
                currentUserService,
                httpClientFactory,
                pipelineProvider,
                resourceManager
            );
        });

        services.AddScoped<IChatService, ChatService>();

        // Register Vietnamese Payment Integrations
        services.AddScoped<IPaymentService, VnPayService>();
        services.AddScoped<IPaymentService, MoMoService>();
        services.AddScoped<IPaymentService, PayOsService>();
        services.AddScoped<IPaymentServiceFactory, PaymentServiceFactory>();
        services.AddScoped<IIdempotentPaymentHandler, IdempotentPaymentHandler>();

        return services;
    }

    /// <summary>
    /// Adds SignalR services for real-time communication.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The modified service collection.</returns>
    /// <exception cref="ArgumentNullException">Thrown if services is null.</exception>
    public static IServiceCollection AddSignalRServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSignalR();
        services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, FloraCore.Infrastructure.Hubs.UserIdProvider>();
        return services;
    }

    /// <summary>
    /// Configures OpenTelemetry for monitoring and tracing.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The modified service collection.</returns>
    /// <exception cref="ArgumentNullException">Thrown if services or configuration is null.</exception>
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bắt buộc cấu hình Strongly-typed và thực hiện xác thực Data Annotations khi khởi động
        services.AddOptions<TelemetryOptions>()
            .Bind(configuration.GetSection(TelemetryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Đọc giá trị cấu hình trực tiếp để thiết lập OpenTelemetry
        var telemetryOptions = new TelemetryOptions();
        configuration.GetSection(TelemetryOptions.SectionName).Bind(telemetryOptions);

        var otlpEndpoint = telemetryOptions.OtlpEndpoint;

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("FloraCore"))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation();

                if (telemetryOptions.ExportToConsole)
                {
                    tracing.AddConsoleExporter();
                }

                if (!string.IsNullOrEmpty(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otlpEndpoint);
                    });
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (telemetryOptions.ExportToConsole)
                {
                    metrics.AddConsoleExporter();
                }

                if (!string.IsNullOrEmpty(otlpEndpoint))
                {
                    metrics.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otlpEndpoint);
                    });
                }
            });

        return services;
    }

    /// <summary>
    /// Configures Hangfire for background task processing.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The modified service collection.</returns>
    /// <exception cref="ArgumentNullException">Thrown if services or configuration is null.</exception>
    public static IServiceCollection AddBackgroundTasks(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var databaseProvider = configuration["DatabaseProvider"] ?? "SqlServer";
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        services.AddHangfire(config =>
        {
            config.SetDataCompatibilityLevel(Hangfire.CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings();

            if (databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            {
                config.UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString));
            }
            else
            {
                config.UseSqlServerStorage(connectionString);
            }
        });

        services.AddHangfireServer();

        return services;
    }
}
