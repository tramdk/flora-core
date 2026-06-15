using FloraCore.Application.Features.PostCategories.Commands;
using FloraCore.Application.Features.PostCategories.Queries;
using FloraCore.Application.Features.PostCategories.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FloraCore.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class PostCategoriesController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    [HttpGet]
    public async Task<ActionResult<List<PostCategoryDto>>> GetAll()
    {
        return await _mediator.Send(new GetPostCategoriesQuery());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PostCategoryDto>> GetById(string id)
    {
        var category = await _mediator.Send(new GetPostCategoryByIdQuery(id));
        if (category == null) return NotFound();
        return category;
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<string>> Create(CreatePostCategoryCommand command)
    {
        var id = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id }, id);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(string id, UpdatePostCategoryCommand command)
    {
        if (id != command.Id) return BadRequest();
        var result = await _mediator.Send(command);
        if (!result) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(string id)
    {
        var result = await _mediator.Send(new DeletePostCategoryCommand(id));
        if (!result) return NotFound();
        return NoContent();
    }
}
