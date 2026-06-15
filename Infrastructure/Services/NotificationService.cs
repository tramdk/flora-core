using System;
using FloraCore.Application.Common.Interfaces;
using FloraCore.Domain.Entities;
using FloraCore.Infrastructure.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace FloraCore.Infrastructure.Services;

/// <summary>
/// Implementation of <see cref="INotificationService"/> for sending notifications to users.
/// </summary>
public class NotificationService(
    IGenericRepository<Notification, Guid> repository,
    IHubContext<NotificationHub, INotificationClient> hubContext,
    ILogger<NotificationService> logger) : INotificationService
{
    private readonly IGenericRepository<Notification, Guid> _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly IHubContext<NotificationHub, INotificationClient> _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    private readonly ILogger<NotificationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Sends a notification to a specific user.
    /// </summary>
    /// <param name="userId">The ID of the user to send the notification to.</param>
    /// <param name="title">The title of the notification.</param>
    /// <param name="message">The message of the notification.</param>
    /// <param name="type">The type of the notification.</param>
    /// <param name="relatedId">The ID of the related entity (optional).</param>
    public async Task SendNotificationToUser(Guid userId, string title, string message, string type, string? relatedId = null)
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Message = message,
            Type = type,
            CreatedAt = DateTime.UtcNow,
            IsRead = false,
            RelatedId = relatedId
        };

        try
        {
            await _repository.AddAsync(notification);
            await _hubContext.Clients.User(userId.ToString()).ReceiveNotification(title, message, type, relatedId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending notification to user {UserId}", userId);
            // Consider re-throwing or handling the exception based on your requirements
        }
    }

    /// <summary>
    /// Sends a notification to all users.
    /// </summary>
    /// <param name="title">The title of the notification.</param>
    /// <param name="message">The message of the notification.</param>
    /// <param name="type">The type of the notification.</param>
    /// <param name="relatedId">The ID of the related entity (optional).</param>
    public async Task SendNotificationToAll(string title, string message, string type, string? relatedId = null)
    {
        try
        {
            await _hubContext.Clients.All.ReceiveNotification(title, message, type, relatedId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending notification to all users.");
            // Consider re-throwing or handling the exception based on your requirements
        }
    }
}
