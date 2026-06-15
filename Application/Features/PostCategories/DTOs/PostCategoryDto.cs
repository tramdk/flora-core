using System;

namespace FloraCore.Application.Features.PostCategories.DTOs;

/// <summary>
/// Data Transfer Object representing a post category.
/// </summary>
public class PostCategoryDto
{
    /// <summary>
    /// The ID of the post category.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The name of the post category.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The timestamp when the category was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The timestamp when the category was last updated.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
