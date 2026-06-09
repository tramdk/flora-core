using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FloraCore.Application.Common.Models;
using FloraCore.Application.Interfaces;
using FloraCore.Domain.Entities;
using MediatR;

namespace FloraCore.Application.Features.WebsiteInfo.Queries;

public class GetAllWebsiteInfoQueryHandler(IWebsiteInfoRepository repository) : IRequestHandler<GetAllWebsiteInfoQuery, IEnumerable<FloraCore.Domain.Entities.WebsiteInfo>>
{
    private readonly IWebsiteInfoRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async Task<IEnumerable<FloraCore.Domain.Entities.WebsiteInfo>> Handle(GetAllWebsiteInfoQuery request, CancellationToken cancellationToken)
    {
        var options = new QueryOptionsBuilder<FloraCore.Domain.Entities.WebsiteInfo>()
            .AsNoTracking()
            .Build();

        return await _repository.GetWithOptionsAsync(options);
    }
}
