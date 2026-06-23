using System;
using MediatR;

namespace FloraCore.Application.Features.WebsiteInfo.Commands;

public record UpdateWebsiteInfoCommand(
    Guid Id,
    string Name,
    string Slogan,
    string Introduction,
    string Email,
    string PhoneNumber,
    string TaxNumber,
    string Location,
    string? ContentConfig = null
) : IRequest
{
    // ThrowIfNull
}
