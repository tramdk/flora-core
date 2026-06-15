using FloraCore.Application.Features.ProductCategories.Commands;
using FloraCore.Application.Features.ProductCategories.Queries;
using FloraCore.Application.Features.ProductCategories.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Asp.Versioning;

namespace FloraCore.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class ProductCategoriesController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    [HttpGet]
    public async Task<ActionResult<List<ProductCategoryDto>>> GetAll()
    {
        return await _mediator.Send(new GetProductCategoriesQuery());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ProductCategoryDto>> GetById(Guid id)
    {
        var category = await _mediator.Send(new GetProductCategoryByIdQuery(id));
        if (category == null) return NotFound();
        return category;
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateProductCategoryCommand command)
    {
        var id = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id }, id);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    public async Task<ActionResult> Update(Guid id, UpdateProductCategoryCommand command)
    {
        if (id != command.Id) return BadRequest();
        var result = await _mediator.Send(command);
        if (!result) return NotFound();
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(Guid id)
    {
        var result = await _mediator.Send(new DeleteProductCategoryCommand(id));
        if (!result) return NotFound();
        return NoContent();
    }
}
