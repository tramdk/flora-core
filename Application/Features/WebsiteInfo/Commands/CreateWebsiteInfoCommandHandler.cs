using System;
using System.Threading;
using System.Threading.Tasks;
using FloraCore.Application.Interfaces;
using FloraCore.Domain.Entities;
using MediatR;

namespace FloraCore.Application.Features.WebsiteInfo.Commands;

public class CreateWebsiteInfoCommandHandler(IWebsiteInfoRepository repository) : IRequestHandler<CreateWebsiteInfoCommand, Guid>
{
    private readonly IWebsiteInfoRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async Task<Guid> Handle(CreateWebsiteInfoCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Name))
        {
            throw new ArgumentException("Name cannot be null or empty.", nameof(request.Name));
        }

        if (!string.IsNullOrEmpty(request.Email) && !request.Email.Contains("@"))
        {
            throw new ArgumentException("Email is not valid.", nameof(request.Email));
        }

        var websiteInfo = new Domain.Entities.WebsiteInfo
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Slogan = request.Slogan,
            Introduction = request.Introduction,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            TaxNumber = request.TaxNumber,
            Location = request.Location,
            ContentConfig = request.ContentConfig,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _repository.AddAsync(websiteInfo);
        return websiteInfo.Id;
    }
}
