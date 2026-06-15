using FloraCore.Application.Features.Users.Queries;
using FloraCore.Application.Features.Users.DTOs;
using FloraCore.Application.Features.Users.DTOs;

namespace FloraCore.Application.Features.Auth.DTOs;

public record AuthResponse(string AccessToken, string RefreshToken, UserDto User)
{
    // ThrowIfNull policy bypass: record/DTO
}
