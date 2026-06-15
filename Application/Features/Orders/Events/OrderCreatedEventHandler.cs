using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Identity;
using FloraCore.Domain.Entities;
using FloraCore.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using FloraCore.Application.Common.Constants;
using FloraCore.Application.Interfaces;
using System;
using FloraCore.Application.Common.Models;
using Microsoft.Extensions.Options;

namespace FloraCore.Application.Features.Orders.Events;

/// <summary>
/// Domain Event Handler to process side effects when an order is created.
/// </summary>
public class OrderCreatedEventHandler(
    UserManager<AppUser> userManager,
    INotificationService notificationService,
    IOrderRepository orderRepository,
    IEmailService emailService,
    IUnitOfWork unitOfWork,
    IGenericRepository<OutboxMessage, Guid> outboxRepository,
    IOptions<PaymentGatewaysOptions> paymentGatewaysOptions,
    IResourceManager resourceManager,
    IPaymentServiceFactory paymentServiceFactory,
    ILogger<OrderCreatedEventHandler> logger) : INotificationHandler<OrderCreatedEvent>
{
    private readonly UserManager<AppUser> _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    private readonly INotificationService _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    private readonly IOrderRepository _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
    private readonly IEmailService _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
    private readonly IUnitOfWork _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    private readonly IGenericRepository<OutboxMessage, Guid> _outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
    private readonly PaymentGatewaysOptions _paymentGatewaysOptions = (paymentGatewaysOptions ?? throw new ArgumentNullException(nameof(paymentGatewaysOptions))).Value;
    private readonly IResourceManager _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
    private readonly IPaymentServiceFactory _paymentServiceFactory = paymentServiceFactory ?? throw new ArgumentNullException(nameof(paymentServiceFactory));
    private readonly ILogger<OrderCreatedEventHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        try
        {
            // Fetch order details
            var order = await _orderRepository.GetByIdAsync(notification.OrderId);
            if (order != null)
            {
                // Stage payment outbox message if it's not COD
                if (order.PaymentMethod != "COD")
                {
                    var apiUrl = _paymentGatewaysOptions.ApiUrl;
                    if (string.IsNullOrEmpty(apiUrl))
                    {
                        throw new InvalidOperationException(_resourceManager.GetString("PaymentConfigMissing"));
                    }

                    var paymentService = _paymentServiceFactory.GetPaymentService(order.PaymentMethod);
                    var returnUrl = paymentService.GetCallbackUrl(apiUrl);

                    var paymentPayload = System.Text.Json.JsonSerializer.Serialize(new OrderPaymentDto
                    {
                        OrderId = order.Id,
                        Amount = order.TotalAmount,
                        Description = $"Thanh toan don hang {order.Id}",
                        ReturnUrl = returnUrl
                    });

                    var outbox = new OutboxMessage
                    {
                        Id = Guid.NewGuid(),
                        Type = "OrderCreatedPaymentEvent",
                        Content = paymentPayload,
                        OccurredOnUtc = DateTime.UtcNow
                    };
                    await _outboxRepository.StageAddAsync(outbox);
                }

                // Fetch user
                var user = await _userManager.FindByIdAsync(order.UserId.ToString());
                if (user != null && !string.IsNullOrEmpty(user.Email))
                {
                    // Send Email to User
                    var emailSubject = "Xác nhận đặt hàng thành công tại Flora Store";
                    var emailBody = $@"
                        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #eee;'>
                            <h2 style='color: #d33; text-align: center;'>Cám ơn bạn đã mua hàng!</h2>
                            <p>Xin chào <strong>{user.FullName}</strong>,</p>
                            <p>Đơn hàng của bạn đã được tiếp nhận thành công. Dưới đây là thông tin chi tiết đơn hàng:</p>
                            <div style='background-color: #f9f9f9; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                                <p style='margin: 5px 0;'><strong>Mã đơn hàng:</strong> {order.Id}</p>
                                <p style='margin: 5px 0;'><strong>Ngày đặt hàng:</strong> {order.OrderDate:dd/MM/yyyy HH:mm:ss}</p>
                                <p style='margin: 5px 0;'><strong>Địa chỉ giao hàng:</strong> {order.ShippingAddress?.Street}, {order.ShippingAddress?.City}</p>
                                <p style='margin: 5px 0;'><strong>Trạng thái:</strong> {order.OrderStatus}</p>
                            </div>
                            <p style='text-align: center; color: #888; font-size: 12px; margin-top: 30px;'>
                                Đây là email tự động từ hệ thống Flora Store. Vui lòng không trả lời trực tiếp email này.
                            </p>
                        </div>";
                    
                    await _emailService.SendEmailAsync(user.Email, emailSubject, emailBody);
                }
            }

            var admins = await _userManager.GetUsersInRoleAsync(RoleConstants.Admin);
            foreach (var admin in admins)
            {
                await _notificationService.SendNotificationToUser(
                    admin.Id,
                    "Đơn hàng mới",
                    $"Một đơn hàng mới đã được tạo với ID: {notification.OrderId}",
                    "OrderCreated",
                    notification.OrderId.ToString());
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending notification for new order.");
        }
    }
}
