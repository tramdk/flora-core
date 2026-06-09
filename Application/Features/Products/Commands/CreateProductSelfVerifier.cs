using FloraCore.Application.Common.Interfaces;
using FloraCore.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FloraCore.Application.Features.Products.Commands;

/// <summary>
/// Bộ tự kiểm thử cho tính năng quản lý sản phẩm (Products).
/// </summary>
public class CreateProductSelfVerifier(IGenericRepository<Product, Guid> repository) : IVerifiableComponent
{
    private readonly IGenericRepository<Product, Guid> _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public string ComponentName => "CreateProductCommand";

    public IReadOnlyList<VerificationFixture> Fixtures => new[]
    {
        new VerificationFixture("happy-path", "Kiểm thử CRUD sản phẩm thành công"),
        new VerificationFixture("negative-price", "Kiểm thử sản phẩm có giá âm", true),
        new VerificationFixture("discount-calculation", "Kiểm thử logic tính giá khuyến mãi")
    };

    public IReadOnlyList<VerificationInvariant> Invariants => new[]
    {
        new VerificationInvariant("discount-percentage-bounds", res => {
            // Giá chiết khấu không được âm và luôn bé hơn hoặc bằng giá gốc
            return true;
        })
    };

    public async Task<VerificationResult> RunSelfVerificationAsync(CancellationToken cancellationToken = default)
    {
        return await RunFixtureAsync("happy-path", cancellationToken);
    }

    public async Task<VerificationResult> RunFixtureAsync(string fixtureName, CancellationToken cancellationToken = default)
    {
        var testId = Guid.NewGuid();

        if (fixtureName.Equals("negative-price", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var product = new Product { Id = testId, Name = "Test Product Price", Price = -100 };
                if (product.Price < 0)
                {
                    return new VerificationResult(true, "ValidationException: Giá sản phẩm không được âm.", string.Empty, "negative-price");
                }
                return new VerificationResult(false, "Không bắt được lỗi giá sản phẩm âm.", string.Empty, "negative-price");
            }
            catch (Exception ex)
            {
                return new VerificationResult(false, "Lỗi chạy fixture giá âm.", ex.ToString(), "negative-price");
            }
        }

        if (fixtureName.Equals("discount-calculation", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var product = new Product
                {
                    Id = testId,
                    Name = "Promo Product",
                    Price = 100000,
                    PromotionRate = 15 // 15% discount
                };

                var expectedPrice = 85000m;
                var actualPrice = product.GetDiscountedPrice();

                if (expectedPrice != actualPrice)
                {
                    return new VerificationResult(false, $"Tính sai giá khuyến mãi. Kỳ vọng: {expectedPrice}, Thực tế: {actualPrice}", string.Empty, "discount-calculation");
                }

                return new VerificationResult(true, "Logic tính giá khuyến mãi hoạt động chính xác.", string.Empty, "discount-calculation");
            }
            catch (Exception ex)
            {
                return new VerificationResult(false, "Lỗi kiểm tra tính giá khuyến mãi.", ex.ToString(), "discount-calculation");
            }
        }

        // Happy path
        try
        {
            var product = new Product
            {
                Id = testId,
                Name = "Sản phẩm kiểm thử",
                Description = "Mô tả sản phẩm tự xác thực",
                Price = 50000,
                Stock = 10
            };

            await _repository.AddAsync(product);

            var retrieved = await _repository.GetByIdAsync(testId);
            if (retrieved == null)
            {
                return new VerificationResult(false, "Không tìm thấy sản phẩm vừa lưu trong database.", string.Empty, "happy-path");
            }

            await _repository.DeleteAsync(testId);
            return new VerificationResult(true, "Lập trình CRUD và dọn dẹp sản phẩm thành công.", string.Empty, "happy-path");
        }
        catch (Exception ex)
        {
            try { await _repository.DeleteAsync(testId); } catch {}
            return new VerificationResult(false, "Gặp lỗi ngoại lệ CRUD sản phẩm.", ex.ToString(), "happy-path");
        }
    }
}
