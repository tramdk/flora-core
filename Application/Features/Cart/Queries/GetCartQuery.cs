using FloraCore.Application.Common.Interfaces;
using FloraCore.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FloraCore.Application.Features.Cart.DTOs;

namespace FloraCore.Application.Features.Cart.Queries;

public record GetCartQuery : IRequest<CartDto>;

public class GetCartQueryHandler(
    IGenericRepository<FloraCore.Domain.Entities.Cart, Guid> cartRepository,
    ICurrentUserService currentUserService,
    IResourceManager resourceManager) : IRequestHandler<GetCartQuery, CartDto>
{
    private readonly IGenericRepository<FloraCore.Domain.Entities.Cart, Guid> _cartRepository = cartRepository ?? throw new ArgumentNullException(nameof(cartRepository));
    private readonly ICurrentUserService _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    private readonly IResourceManager _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));

    public async Task<CartDto> Handle(GetCartQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId ?? throw new UnauthorizedAccessException(_resourceManager.GetString("UserNotAuthenticated"));

        var cart = await _cartRepository.GetQueryable()
            .Include(c => c.Items)
            .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (cart == null)
        {
            return new CartDto { UserId = userId };
        }

        return new CartDto
        {
            UserId = userId,
            Items = cart.Items.Select(i => new CartItemDto
            {
                ProductId = i.ProductId,
                ProductName = i.Product.Name,
                OriginalPrice = i.Product.Price,
                Price = i.Product.GetDiscountedPrice(),
                PromotionRate = i.Product.PromotionRate,
                Quantity = i.Quantity,
                ImageUrl = i.Product.ImageUrl
            }).ToList()
        };
    }
}
