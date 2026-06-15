using System;
using System.Collections.Generic;

namespace FloraCore.Application.Features.Users.DTOs;

/// <summary>
/// Data Transfer Object representing a user profile.
/// </summary>
public class UserDto
{
    /// <summary>
    /// The user ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The user's email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// The user's full name.
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// The user's username.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// The roles assigned to the user.
    /// </summary>
    public IList<string> Roles { get; set; } = new List<string>();
}
