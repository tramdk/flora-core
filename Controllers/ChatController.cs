using FloraCore.Application.Features.Chat.Commands.SendMessage;
using FloraCore.Application.Features.Chat.Queries.GetChatHistory;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Asp.Versioning;

namespace FloraCore.Controllers;

[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class ChatController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    [HttpPost("send")]
    public async Task<ActionResult<Guid>> SendMessage(SendMessageCommand command)
    {
        return Ok(await _mediator.Send(command));
    }

    [HttpGet("history/{otherUserId}")]
    public async Task<ActionResult<List<ChatMessageDto>>> GetChatHistory(Guid otherUserId)
    {
        return Ok(await _mediator.Send(new GetChatHistoryQuery(otherUserId)));
    }
}
