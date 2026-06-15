using System;

namespace FloraCore.Application.Features.ProductCategories.DTOs;

/// <summary>
/// Data Transfer Object representing a product category.
/// </summary>
public class ProductCategoryDto
{
    /// <summary>
    /// The ID of the product category.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The name of the product category.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The description of the product category.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The image URL of the product category.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// The timestamp when the category was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
