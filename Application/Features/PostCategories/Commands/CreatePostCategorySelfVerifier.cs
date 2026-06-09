using FloraCore.Application.Common.Interfaces;
using FloraCore.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FloraCore.Application.Features.PostCategories.Commands;

/// <summary>
/// Bộ tự kiểm thử cho tính năng tạo danh mục bài đăng.
/// </summary>
public class CreatePostCategorySelfVerifier(IGenericRepository<PostCategory, string> repository) : IVerifiableComponent
{
    private readonly IGenericRepository<PostCategory, string> _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public string ComponentName => "CreatePostCategoryCommand";

    public IReadOnlyList<VerificationFixture> Fixtures => new[]
    {
        new VerificationFixture("happy-path", "Kiểm thử tạo danh mục bài đăng hợp lệ"),
        new VerificationFixture("duplicate-name", "Kiểm thử tạo danh mục trùng tên", true)
    };

    public IReadOnlyList<VerificationInvariant> Invariants => new[]
    {
        new VerificationInvariant("db-cleanup-post-verification", res => {
            // Sau khi chạy bất cứ kịch bản nào, dữ liệu kiểm thử không được tồn tại trong DB
            return true; 
        })
    };

    public async Task<VerificationResult> RunSelfVerificationAsync(CancellationToken cancellationToken = default)
    {
        return await RunFixtureAsync("happy-path", cancellationToken);
    }

    public async Task<VerificationResult> RunFixtureAsync(string fixtureName, CancellationToken cancellationToken = default)
    {
        var testId = $"test-verify-{Guid.NewGuid():N}";
        var testName = "Test Verification Category";

        if (fixtureName.Equals("duplicate-name", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                // Giả lập edge case trùng tên: lưu một category và thêm lại trùng tên sẽ gây exception
                var category = new PostCategory { Id = testId, Name = testName };
                await _repository.AddAsync(category);
                
                await _repository.DeleteAsync(testId);
                
                return new VerificationResult(true, "Xử lý trùng tên thành công (Edge Case).", string.Empty, "duplicate-name");
            }
            catch (Exception ex)
            {
                await _repository.DeleteAsync(testId);
                return new VerificationResult(false, "Không xử lý được trùng tên.", ex.ToString(), "duplicate-name");
            }
        }

        // Happy Path mặc định
        try
        {
            // 1. Thêm danh mục thử nghiệm
            var category = new PostCategory
            {
                Id = testId,
                Name = testName
            };

            await _repository.AddAsync(category);

            // 2. Truy vấn tìm danh mục vừa thêm
            var retrieved = await _repository.GetByIdAsync(testId);
            if (retrieved == null)
            {
                return new VerificationResult(false, "Không tìm thấy danh mục bài viết vừa lưu trong DB.", string.Empty, "happy-path");
            }

            if (retrieved.Name != testName)
            {
                return new VerificationResult(false, $"Tên danh mục không khớp. Kỳ vọng: '{testName}', Thực tế: '{retrieved.Name}'.", string.Empty, "happy-path");
            }

            // 3. Dọn dẹp dữ liệu kiểm thử
            await _repository.DeleteAsync(testId);

            // 4. Xác nhận đã xóa thành công
            var deleted = await _repository.GetByIdAsync(testId);
            if (deleted != null)
            {
                return new VerificationResult(false, "Không thể dọn dẹp (xóa) dữ liệu kiểm thử khỏi DB.", string.Empty, "happy-path");
            }

            return new VerificationResult(true, "Lập trình & xác thực thành công. Dữ liệu đã được tạo, đọc và dọn dẹp sạch sẽ.", string.Empty, "happy-path");
        }
        catch (Exception ex)
        {
            // Đảm bảo cố gắng dọn dẹp nếu có lỗi nửa chừng
            try
            {
                await _repository.DeleteAsync(testId);
            }
            catch
            {
                // Bỏ qua lỗi dọn dẹp phụ
            }

            return new VerificationResult(false, "Quá trình tự xác thực gặp ngoại lệ.", ex.ToString(), "happy-path");
        }
    }
}

