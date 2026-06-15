using System;
using System.Collections.Generic;
using System.Linq;

namespace FloraCore.Application.Features.Cart.DTOs;

/// <summary>
/// Data Transfer Object representing the user's cart.
/// </summary>
public record CartDto
{
    /// <summary>
    /// The user ID of the cart owner.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// The items currently in the cart.
    /// </summary>
    public List<CartItemDto> Items { get; init; } = new();

    /// <summary>
    /// The total price of the items in the cart.
    /// </summary>
    public decimal TotalPrice => Items.Sum(i => i.Price * i.Quantity);
}

/// <summary>
/// Data Transfer Object representing an item in the cart.
/// </summary>
public record CartItemDto
{
    /// <summary>
    /// The product ID.
    /// </summary>
    public Guid ProductId { get; init; }

    /// <summary>
    /// The name of the product.
    /// </summary>
    public string ProductName { get; init; } = string.Empty;

    /// <summary>
    /// The original price of the product.
    /// </summary>
    public decimal OriginalPrice { get; init; }

    /// <summary>
    /// The price of the product.
    /// </summary>
    public decimal Price { get; init; }

    /// <summary>
    /// The promotion rate of the product.
    /// </summary>
    public decimal PromotionRate { get; init; }

    /// <summary>
    /// The quantity of the item in the cart.
    /// </summary>
    public int Quantity { get; init; }

    /// <summary>
    /// The URL of the product image.
    /// </summary>
    public string? ImageUrl { get; init; }
}
