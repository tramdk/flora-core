using FloraCore.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Identity;
using System;
using System.Threading;
using System.Threading.Tasks;

using FloraCore.Application.Features.Users.DTOs;

using FloraCore.Application.Features.Users.DTOs;

namespace FloraCore.Application.Features.Users.Queries;

public record GetUserByIdQuery(Guid Id) : IRequest<UserDto?>
{
    // ThrowIfNull
}

public class GetUserByIdHandler(UserManager<AppUser> userManager) : IRequestHandler<GetUserByIdQuery, UserDto?>
{
    private readonly UserManager<AppUser> _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));

    public async Task<UserDto?> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.Id.ToString());
        if (user == null) return null;

        var roles = await _userManager.GetRolesAsync(user);
        return new UserDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            UserName = user.UserName ?? string.Empty,
            Roles = roles
        };
    }
}
