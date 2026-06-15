using System;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FloraCore.Application.Features.Cart.Commands;
using FloraCore.Application.Features.Cart.Queries;
using FloraCore.Application.Features.Cart.DTOs;
using FloraCore.Application.Features.Cart.DTOs;
using FloraCore.Application.Features.Auth.Commands;
using FloraCore.Domain.Entities;
using FloraCore.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FloraCore.Tests.IntegrationTests;

public class CartDiscountIntegrationTests : BaseIntegrationTest
{
    public CartDiscountIntegrationTests(CustomWebApplicationFactory factory) : base(factory) { }

    private async Task<string> GetTokenAsync(string email = "cart_user@example.com", string password = "Password123!")
    {
        var registerCommand = new RegisterCommand(email, password, "Cart User");
        await _client.PostAsJsonAsync("/api/v1/auth/register", registerCommand);

        var loginCommand = new LoginCommand(email, password);
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", loginCommand);
        
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            throw new Exception($"Login failed in GetTokenAsync. Status: {response.StatusCode}, Content: {content}");
        }

        var result = await GetResponseDataAsync<LoginResponse>(response);
        return result!.AccessToken;
    }

    private record LoginResponse(string AccessToken, string RefreshToken);

    [Fact]
    public async Task AddToCartAndGetCart_ShouldApplyPromotionDiscount()
    {
        // 1. Authenticate user
        var token = await GetTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 2. Create product directly in database
        var productId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            Name = "Promotion Product",
            Description = "A product with discount",
            Price = 150m,
            PromotionRate = 20m, // 20% discount -> Price = 120m
            Stock = 50
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        // 3. Add to cart via API
        var addToCartCmd = new AddToCartCommand(productId, 3);
        var addResp = await _client.PostAsJsonAsync("/api/v1/cart/add", addToCartCmd);
        Assert.Equal(HttpStatusCode.OK, addResp.StatusCode);

        // 4. Get cart via API and verify discount is applied
        var getCartResp = await _client.GetAsync("/api/v1/cart");
        Assert.Equal(HttpStatusCode.OK, getCartResp.StatusCode);

        var cart = await GetResponseDataAsync<CartDto>(getCartResp);
        Assert.NotNull(cart);
        Assert.Single(cart.Items);

        var item = cart.Items.First();
        Assert.Equal(productId, item.ProductId);
        Assert.Equal("Promotion Product", item.ProductName);
        Assert.Equal(150m, item.OriginalPrice);
        Assert.Equal(20m, item.PromotionRate);
        Assert.Equal(120m, item.Price);
        Assert.Equal(3, item.Quantity);

        // TotalPrice: 120m * 3 = 360m
        Assert.Equal(360m, cart.TotalPrice);
    }
}
