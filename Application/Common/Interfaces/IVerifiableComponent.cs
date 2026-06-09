using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FloraCore.Application.Common.Interfaces;

/// <summary>
/// Định nghĩa một kịch bản kiểm thử cụ thể (Verification Fixture).
/// </summary>
public record VerificationFixture(string Name, string Description, bool IsProbe = false);

/// <summary>
/// Định nghĩa một điều kiện bất biến cần kiểm tra (Verification Invariant).
/// </summary>
public record VerificationInvariant(string Name, Func<VerificationResult, bool> Predicate);

/// <summary>
/// Định nghĩa một thành phần tự kiểm thử (Verifiable Component) trong hệ thống.
/// </summary>
public interface IVerifiableComponent
{
    /// <summary>
    /// Tên của thành phần cần tự xác thực.
    /// </summary>
    string ComponentName { get; }

    /// <summary>
    /// Danh sách các kịch bản kiểm thử của thành phần này.
    /// </summary>
    IReadOnlyList<VerificationFixture> Fixtures => new[] { new VerificationFixture("default", "Kịch bản tự kiểm thử mặc định") };

    /// <summary>
    /// Danh sách các điều kiện bất biến áp dụng cho thành phần này.
    /// </summary>
    IReadOnlyList<VerificationInvariant> Invariants => Array.Empty<VerificationInvariant>();

    /// <summary>
    /// Thực thi kịch bản kiểm thử tự động của chính thành phần này.
    /// </summary>
    Task<VerificationResult> RunSelfVerificationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Thực thi một kịch bản kiểm thử cô lập cụ thể.
    /// </summary>
    Task<VerificationResult> RunFixtureAsync(string fixtureName, CancellationToken cancellationToken = default)
    {
        return RunSelfVerificationAsync(cancellationToken);
    }
}

/// <summary>
/// Kết quả trả về của quá trình tự kiểm thử.
/// </summary>
public record VerificationResult(bool Success, string Message, string Details = "", string FixtureName = "default")
{
    public string Message { get; init; } = Message ?? throw new ArgumentNullException(nameof(Message));
    public string Details { get; init; } = Details ?? throw new ArgumentNullException(nameof(Details));
    public string FixtureName { get; init; } = FixtureName ?? throw new ArgumentNullException(nameof(FixtureName));
}

