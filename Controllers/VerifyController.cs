using FloraCore.Application.Common.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Asp.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FloraCore.Controllers;

/// <summary>
/// Controller cung cấp API tự kiểm thử (Self-Verification) dành riêng cho môi trường phát triển.
/// Luôn trả về JSON. Giao diện Dashboard HTML được phục vụ tĩnh tại /verify/index.html.
/// Tích hợp đầy đủ mô hình AI Diagnostic Hints theo chuẩn Anthropic London 2026.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiExplorerSettings(IgnoreApi = true)]
public class VerifyController(IEnumerable<IVerifiableComponent> verifiableComponents, IWebHostEnvironment env) : ControllerBase
{
    private readonly IEnumerable<IVerifiableComponent> _verifiableComponents = verifiableComponents ?? throw new ArgumentNullException(nameof(verifiableComponents));
    private readonly IWebHostEnvironment _env = env ?? throw new ArgumentNullException(nameof(env));

    /// <summary>
    /// Chạy toàn bộ các thành phần tự kiểm thử được đăng ký trong hệ thống.
    /// Luôn trả về kết quả dạng JSON. Dashboard HTML truy cập qua /verify/.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> RunAllVerifications(CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment() && !_env.IsEnvironment("Testing"))
        {
            return NotFound(new { Message = "Endpoint này chỉ khả dụng trên môi trường Development hoặc Testing." });
        }

        var results = new List<VerificationItemResult>();
        var components = _verifiableComponents.ToList();

        int passed = 0;
        int failed = 0;

        foreach (var component in components)
        {
            var res = await component.RunSelfVerificationAsync(cancellationToken);
            if (res.Success)
            {
                passed++;
            }
            else
            {
                failed++;
            }

            var diagnosticHint = res.Success ? string.Empty : GenerateAiDiagnosticHint(res.Details, res.Message);

            results.Add(new VerificationItemResult(
                component.ComponentName,
                res.Success,
                res.Message,
                res.Details,
                diagnosticHint,
                component.Fixtures.Select(f => f.Name).ToList(),
                res.FixtureName
            ));
        }

        var simulateFailure = Request.Query["simulateFailure"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase) ||
                             Request.Query["breakContract"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

        if (simulateFailure)
        {
            var simulatedErrorDetails = "Npgsql.NpgsqlException (0x80004005): Failed to connect to 127.0.0.1:5432\n" +
                                        " ---> System.Net.Sockets.SocketException (10061): No connection could be made because the target machine actively refused it.\n" +
                                        "   at Npgsql.Internal.NpgsqlConnector.Connect(CancellationToken cancellationToken)";
            
            var diagnosticHint = GenerateAiDiagnosticHint(simulatedErrorDetails, "Kết nối Postgres bị từ chối.");
            
            results.Add(new VerificationItemResult(
                "DatabaseConnectionVerifier",
                false,
                "Lỗi kết nối cơ sở dữ liệu: SocketException.",
                simulatedErrorDetails,
                diagnosticHint,
                new List<string> { "default" },
                "default"
            ));
            
            failed++;
        }

        var overallSuccess = failed == 0;
        var totalCount = components.Count + (simulateFailure ? 1 : 0);

        return Ok(new
        {
            Success = overallSuccess,
            TotalComponents = totalCount,
            Passed = passed,
            Failed = failed,
            Results = results
        });
    }

    /// <summary>
    /// Chạy riêng toàn bộ các fixtures của 1 component cụ thể.
    /// </summary>
    [HttpGet("{componentName}")]
    public async Task<IActionResult> RunComponentVerifications(string componentName, CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment() && !_env.IsEnvironment("Testing"))
        {
            return NotFound(new { Message = "Endpoint này chỉ khả dụng trên môi trường Development hoặc Testing." });
        }

        var component = _verifiableComponents.FirstOrDefault(c => c.ComponentName.Equals(componentName, StringComparison.OrdinalIgnoreCase));
        if (component == null)
        {
            return NotFound(new { Message = $"Không tìm thấy verifiable component: {componentName}" });
        }

        var results = new List<VerificationItemResult>();
        int passed = 0;
        int failed = 0;

        foreach (var fixture in component.Fixtures)
        {
            var res = await component.RunFixtureAsync(fixture.Name, cancellationToken);
            if (res.Success)
            {
                passed++;
            }
            else
            {
                failed++;
            }

            var diagnosticHint = res.Success ? string.Empty : GenerateAiDiagnosticHint(res.Details, res.Message);

            results.Add(new VerificationItemResult(
                component.ComponentName,
                res.Success,
                res.Message,
                res.Details,
                diagnosticHint,
                component.Fixtures.Select(f => f.Name).ToList(),
                fixture.Name
            ));
        }

        return Ok(new
        {
            Success = failed == 0,
            TotalComponents = component.Fixtures.Count,
            Passed = passed,
            Failed = failed,
            Results = results
        });
    }

    /// <summary>
    /// Chạy 1 fixture cụ thể của 1 component cụ thể.
    /// </summary>
    [HttpGet("{componentName}/{fixtureName}")]
    public async Task<IActionResult> RunFixtureVerification(string componentName, string fixtureName, CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment() && !_env.IsEnvironment("Testing"))
        {
            return NotFound(new { Message = "Endpoint này chỉ khả dụng trên môi trường Development hoặc Testing." });
        }

        var component = _verifiableComponents.FirstOrDefault(c => c.ComponentName.Equals(componentName, StringComparison.OrdinalIgnoreCase));
        if (component == null)
        {
            return NotFound(new { Message = $"Không tìm thấy verifiable component: {componentName}" });
        }

        var fixture = component.Fixtures.FirstOrDefault(f => f.Name.Equals(fixtureName, StringComparison.OrdinalIgnoreCase));
        if (fixture == null)
        {
            return NotFound(new { Message = $"Không tìm thấy fixture '{fixtureName}' trong component '{componentName}'" });
        }

        var res = await component.RunFixtureAsync(fixture.Name, cancellationToken);
        var diagnosticHint = res.Success ? string.Empty : GenerateAiDiagnosticHint(res.Details, res.Message);

        var item = new VerificationItemResult(
            component.ComponentName,
            res.Success,
            res.Message,
            res.Details,
            diagnosticHint,
            component.Fixtures.Select(f => f.Name).ToList(),
            fixture.Name
        );

        return Ok(new
        {
            Success = res.Success,
            TotalComponents = 1,
            Passed = res.Success ? 1 : 0,
            Failed = res.Success ? 0 : 1,
            Results = new List<VerificationItemResult> { item }
        });
    }

    private record VerificationItemResult(
        string ComponentName, 
        bool Success, 
        string Message, 
        string Details, 
        string AiDiagnosticHint,
        List<string> AvailableFixtures,
        string ActiveFixture
    );

    /// <summary>
    /// Tạo ra gợi ý chẩn đoán tự động dành riêng cho AI Agent (AI-friendly Diagnostic Hints) khi phát hiện test bị gãy.
    /// </summary>
    private static string GenerateAiDiagnosticHint(string details, string message)
    {
        if (string.IsNullOrEmpty(details) && string.IsNullOrEmpty(message)) return string.Empty;

        var fullText = (details ?? string.Empty) + " " + (message ?? string.Empty);

        if (fullText.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) || 
            fullText.Contains("SocketException", StringComparison.OrdinalIgnoreCase) || 
            fullText.Contains("SqlException", StringComparison.OrdinalIgnoreCase) || 
            fullText.Contains("PostgresException", StringComparison.OrdinalIgnoreCase))
        {
            return "🔍 [AI DIAGNOSTIC]: Database connection failure detected. Verify PostgreSQL/SQL Server connection string in .env (DB_PASSWORD), check if DB service container is running, or verify migrations are applied.";
        }
        if (fullText.Contains("Redis", StringComparison.OrdinalIgnoreCase) || 
            fullText.Contains("RedisConnectionException", StringComparison.OrdinalIgnoreCase))
        {
            return "🔍 [AI DIAGNOSTIC]: Redis connection failure. Check REDIS_CONNECTION settings in .env or ensure Redis service container is started.";
        }
        if (fullText.Contains("ValidationException", StringComparison.OrdinalIgnoreCase) || 
            fullText.Contains("validation", StringComparison.OrdinalIgnoreCase))
        {
            return "🔍 [AI DIAGNOSTIC]: Model validation contract broken. Check that input fields comply with FluentValidation rules defined in the corresponding validators.";
        }
        if (fullText.Contains("NullReferenceException", StringComparison.OrdinalIgnoreCase))
        {
            return "🔍 [AI DIAGNOSTIC]: Null reference exception. Check if all required dependencies are registered in DI Container (ServiceCollectionExtensions.cs) and ensure primary constructors are correctly instantiated.";
        }
        if (fullText.Contains("UnauthorizedAccessException", StringComparison.OrdinalIgnoreCase) || 
            fullText.Contains("SecurityException", StringComparison.OrdinalIgnoreCase))
        {
            return "🔍 [AI DIAGNOSTIC]: Security access denied. Check user role, claims permissions, or JWT authorization header configuration.";
        }

        return "🔍 [AI DIAGNOSTIC]: Verification failed. Review tech logs, ensure stage changes are staged before SaveChangesAsync, and check if database seeding exists.";
    }
}

