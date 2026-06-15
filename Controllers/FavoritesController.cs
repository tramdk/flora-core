using FloraCore.Application.Features.Favorites.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

using Asp.Versioning;

namespace FloraCore.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class FavoritesController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    [HttpPost("toggle/{productId}")]
    public async Task<ActionResult<bool>> ToggleFavorite(Guid productId)
    {
        return Ok(await _mediator.Send(new ToggleFavoriteCommand(productId)));
    }
}
