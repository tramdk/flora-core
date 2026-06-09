using System;

namespace FloraCore.Domain.Entities;

public class Post
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Guid AuthorId { get; set; }
    public AppUser Author { get; set; } = null!;
    public double AverageRating { get; private set; }
    public int TotalRatings { get; private set; }
    public string? CategoryId { get; set; }
    public PostCategory? Category { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsApproved { get; set; } = true;

    public void AddRating(int score)
    {
        if (score < 1 || score > 5) throw new ArgumentOutOfRangeException(nameof(score));
        
        double currentTotalScore = AverageRating * TotalRatings;
        TotalRatings++;
        AverageRating = (currentTotalScore + score) / TotalRatings;
    }
}
