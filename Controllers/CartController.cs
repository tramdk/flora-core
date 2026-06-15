using FloraCore.Application.Features.Cart.Commands;
using FloraCore.Application.Features.Cart.Queries;
using FloraCore.Application.Features.Cart.DTOs;
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
public class CartController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    [HttpGet]
    public async Task<ActionResult<CartDto>> GetCart()
    {
        return Ok(await _mediator.Send(new GetCartQuery()));
    }

    [HttpPost("add")]
    public async Task<ActionResult> AddToCart(AddToCartCommand command)
    {
        await _mediator.Send(command);
        return Ok();
    }

    // [HttpPut("update-quantity")]
    // public async Task<ActionResult> UpdateQuantity(UpdateCartItemQuantityCommand command)
    // {
    //     await _mediator.Send(command);
    //     return Ok();
    // }

    [HttpPut("update")]
    public async Task<ActionResult> UpdateQuantity(UpdateCartItemQuantityCommand command)
    {
        await _mediator.Send(command);
        return Ok();
    }

    [HttpDelete("remove/{productId}")]
    public async Task<ActionResult> RemoveFromCart(Guid productId)
    {
        await _mediator.Send(new RemoveFromCartCommand(productId));
        return NoContent();
    }
}
