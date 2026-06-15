using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System;
using FloraCore.Application.Common.Interfaces;

namespace FloraCore.Application.Common.Behaviors;

/// <summary>
/// Pipeline behavior to enforce request idempotency for marked commands.
/// </summary>
public class IdempotencyBehavior<TRequest, TResponse>(IDistributedCache cache, IResourceManager resourceManager) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IDistributedCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly IResourceManager _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IIdempotentCommand idempotentCommand)
        {
            return await next();
        }

        var key = idempotentCommand.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            return await next();
        }

        var cacheKey = $"idempotency:{key}";

        // Check cache for existing request status or result
        var cachedValue = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (cachedValue != null)
        {
            if (cachedValue == "processing")
            {
                throw new InvalidOperationException(_resourceManager.GetString("DuplicateRequest"));
            }

            var cachedResponse = JsonSerializer.Deserialize<TResponse>(cachedValue);
            if (cachedResponse != null)
            {
                return cachedResponse;
            }
        }

        // Set status as processing
        var processingOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
        };
        await _cache.SetStringAsync(cacheKey, "processing", processingOptions, cancellationToken);

        try
        {
            var response = await next();

            // Store response in cache with 24 hours expiry
            var completedOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
            };
            var serializedResponse = JsonSerializer.Serialize(response);
            await _cache.SetStringAsync(cacheKey, serializedResponse, completedOptions, cancellationToken);

            return response;
        }
        catch (Exception)
        {
            // Remove processing marker on failure to allow retries
            await _cache.RemoveAsync(cacheKey, cancellationToken);
            throw;
        }
    }
}
