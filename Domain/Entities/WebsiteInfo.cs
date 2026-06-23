using System;

namespace FloraCore.Domain.Entities;

public class WebsiteInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slogan { get; set; } = string.Empty;
    public string Introduction { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string TaxNumber { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string? ContentConfig { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}