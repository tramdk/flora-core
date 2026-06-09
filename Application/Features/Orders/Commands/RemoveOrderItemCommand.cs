using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FloraCore.Application.Common.Interfaces;
using FloraCore.Application.Interfaces;
using FloraCore.Domain.Entities;
using MediatR;

namespace FloraCore.Application.Features.Orders.Commands;

public record RemoveOrderItemCommand(Guid OrderId, Guid OrderItemId) : IRequest<bool>;

public class RemoveOrderItemCommandHandler : IRequestHandler<RemoveOrderItemCommand, bool>
{
    private readonly IOrderRepository _repository;

    public RemoveOrderItemCommandHandler(IOrderRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<bool> Handle(RemoveOrderItemCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId);
        if (order == null) return false;

        var orderItem = order.OrderItems.FirstOrDefault(i => i.Id == request.OrderItemId);
        if (orderItem == null) return false;

        order.TotalAmount -= orderItem.Price * orderItem.Quantity;
        await _repository.UpdateAsync(order);
        await _repository.DeleteOrderItemAsync(orderItem);
        return true;
    }
}
