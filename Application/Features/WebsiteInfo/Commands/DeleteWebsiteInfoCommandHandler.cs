using FloraCore.Application.Common.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using FloraCore.Application.Interfaces;
using FloraCore.Domain.Entities;
using MediatR;

namespace FloraCore.Application.Features.WebsiteInfo.Commands;

public class DeleteWebsiteInfoCommandHandler(IWebsiteInfoRepository repository, IResourceManager resourceManager) : IRequestHandler<DeleteWebsiteInfoCommand>
{
    private readonly IWebsiteInfoRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly IResourceManager _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));

    public async Task Handle(DeleteWebsiteInfoCommand request, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetByIdAsync(request.Id);
        if (existing == null)
        {
            throw new ArgumentException(_resourceManager.GetString("WebsiteInfoNotFound"));
        }

        await _repository.DeleteAsync(request.Id);
    }
}
