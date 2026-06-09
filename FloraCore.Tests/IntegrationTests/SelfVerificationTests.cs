using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;
using FloraCore.Application.Common.Interfaces;
using System.Linq;

namespace FloraCore.Tests.IntegrationTests;

/// <summary>
/// Các ca kiểm thử tích hợp cho hệ thống tự xác thực Verifiable Component.
/// Bao phủ cả 3 kịch bản: Happy Path, Failure Simulation, và JSON API Contract.
/// </summary>
public class SelfVerificationTests : BaseIntegrationTest
{
    public SelfVerificationTests(CustomWebApplicationFactory factory) : base(factory) { }

    /// <summary>
    /// Happy Path: Tất cả các thành phần tự kiểm thử vượt qua thành công.
    /// </summary>
    [Fact]
    public async Task RunAllVerifications_ReturnsSuccessAndAllComponentsPassed()
    {
        // Act - Gọi API chạy toàn bộ các thành phần tự kiểm thử
        var response = await _client.GetAsync("/api/v1/verify");

        // Assert - Đảm bảo API trả về OK
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Đọc kết quả được giải nén qua GetResponseDataAsync
        var verificationReport = await GetResponseDataAsync<VerifyReportResponse>(response);

        Assert.NotNull(verificationReport);
        Assert.True(verificationReport.Success, "Có thành phần tự kiểm thử bị thất bại!");
        Assert.True(verificationReport.TotalComponents >= 3, "Không tìm thấy đủ 3 Verifiable Component đăng ký trong DI.");
        Assert.Equal(0, verificationReport.Failed);
        Assert.Equal(verificationReport.TotalComponents, verificationReport.Passed);

        // Kiểm tra chi tiết từng kết quả
        foreach (var result in verificationReport.Results)
        {
            Assert.True(result.Success, $"Component '{result.ComponentName}' tự kiểm thử thất bại: {result.Message}");
        }
    }

    /// <summary>
    /// Failure Simulation: Mô phỏng bẻ gãy hợp đồng để kiểm tra AI Diagnostic Hints.
    /// </summary>
    [Fact]
    public async Task RunAllVerifications_WithSimulatedFailure_ReturnsFailureWithDiagnosticHint()
    {
        // Act - Kích hoạt bẻ gãy hợp đồng qua simulateFailure=true
        var response = await _client.GetAsync("/api/v1/verify?simulateFailure=true");

        // Assert - API vẫn trả về OK vì chạy hoàn tất, nhưng báo cáo thất bại
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var verificationReport = await GetResponseDataAsync<VerifyReportResponse>(response);

        Assert.NotNull(verificationReport);
        Assert.False(verificationReport.Success, "Lẽ ra phải báo cáo thất bại tổng thể khi có simulateFailure=true.");
        Assert.True(verificationReport.Failed > 0, "Số lượng lỗi phải lớn hơn 0.");

        // Tìm thành phần DatabaseConnectionVerifier bị bẻ gãy
        var failedComponent = verificationReport.Results.FirstOrDefault(r => r.ComponentName == "DatabaseConnectionVerifier");
        Assert.NotNull(failedComponent);
        Assert.False(failedComponent.Success);
        Assert.Contains("Npgsql", failedComponent.Details);
        
        // Xác nhận Claude chẩn đoán tự động (AI-friendly Diagnostic Hints) hoạt động đúng đắn
        Assert.Contains("[AI DIAGNOSTIC]: Database connection failure detected", failedComponent.AiDiagnosticHint);
    }

    /// <summary>
    /// JSON API Contract: Xác nhận cấu trúc JSON response đáp ứng đúng contract cho client-side rendering.
    /// Dashboard HTML tĩnh (/verify/) phụ thuộc vào contract này để render giao diện.
    /// </summary>
    [Fact]
    public async Task RunAllVerifications_JsonApiContract_ContainsAllRequiredFields()
    {
        // Act - Gọi API để kiểm tra JSON contract
        var response = await _client.GetAsync("/api/v1/verify");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var report = await GetResponseDataAsync<VerifyReportResponse>(response);

        // Assert - Contract bắt buộc: các trường cấp cao
        Assert.NotNull(report);
        Assert.True(report.TotalComponents >= 3, "TotalComponents phải >= 3.");
        Assert.True(report.Passed >= 0, "Passed phải >= 0.");
        Assert.True(report.Failed >= 0, "Failed phải >= 0.");
        Assert.Equal(report.TotalComponents, report.Passed + report.Failed);
        Assert.NotNull(report.Results);
        Assert.Equal(report.TotalComponents, report.Results.Count);

        // Assert - Contract bắt buộc: cấu trúc từng kết quả
        foreach (var result in report.Results)
        {
            Assert.False(string.IsNullOrWhiteSpace(result.ComponentName), "ComponentName không được rỗng.");
            Assert.False(string.IsNullOrWhiteSpace(result.Message), "Message không được rỗng.");
            // Details có thể rỗng khi thành công, nhưng field phải tồn tại
            Assert.NotNull(result.Details);
        }

        // Act - Gọi API với simulateFailure để kiểm tra contract khi có lỗi
        var failResponse = await _client.GetAsync("/api/v1/verify?simulateFailure=true");
        var failReport = await GetResponseDataAsync<VerifyReportResponse>(failResponse);

        Assert.NotNull(failReport);
        Assert.False(failReport.Success);

        // Assert - Contract bắt buộc: failed component phải có AI diagnostic hint
        var failedItem = failReport.Results.FirstOrDefault(r => !r.Success);
        Assert.NotNull(failedItem);
        Assert.False(string.IsNullOrWhiteSpace(failedItem.AiDiagnosticHint), 
            "AiDiagnosticHint phải có nội dung khi component thất bại.");
        Assert.False(string.IsNullOrWhiteSpace(failedItem.Details), 
            "Details phải có nội dung khi component thất bại.");
    }

    /// <summary>
    /// Isolation Check: Đảm bảo chạy riêng 1 component/fixture hoạt động hoàn hảo.
    /// </summary>
    [Fact]
    public async Task RunComponentIsolation_ReturnsCorrectReport()
    {
        // Act - Chạy cô lập CreateProductCategoryCommand
        var response = await _client.GetAsync("/api/v1/verify/CreateProductCategoryCommand");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var report = await GetResponseDataAsync<VerifyReportResponse>(response);
        Assert.NotNull(report);
        Assert.True(report.Success);
        Assert.Equal(3, report.TotalComponents); // 3 fixtures
        Assert.Equal(3, report.Results.Count);

        // Act - Chạy cô lập 1 fixture cụ thể
        var responseFixture = await _client.GetAsync("/api/v1/verify/CreateProductCategoryCommand/empty-name-validation");
        Assert.Equal(HttpStatusCode.OK, responseFixture.StatusCode);

        var reportFixture = await GetResponseDataAsync<VerifyReportResponse>(responseFixture);
        Assert.NotNull(reportFixture);
        Assert.True(reportFixture.Success);
        Assert.Equal(1, reportFixture.TotalComponents);
        Assert.Equal("empty-name-validation", reportFixture.Results.First().ActiveFixture);
    }

    private record VerifyReportResponse(
        bool Success,
        int TotalComponents,
        int Passed,
        int Failed,
        System.Collections.Generic.List<VerifyItemResponse> Results
    );

    private record VerifyItemResponse(
        string ComponentName,
        bool Success,
        string Message,
        string Details,
        string AiDiagnosticHint,
        System.Collections.Generic.List<string> AvailableFixtures,
        string ActiveFixture
    );
}

