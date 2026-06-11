namespace FloraCore.Domain.Exceptions;

/// <summary>
/// Base exception for all domain-specific exceptions.
/// </summary>
public abstract class DomainException : Exception
{
    public string Code { get; }

    protected DomainException(string code, string message) : base(message)
    {
        Code = code;
    }

    protected DomainException(string code, string message, Exception innerException) 
        : base(message, innerException)
    {
        Code = code;
    }
}

/// <summary>
/// Exception thrown when an entity is not found.
/// </summary>
public class EntityNotFoundException(string entityType, object entityId)
    : DomainException("ENTITY_NOT_FOUND", $"{entityType} with ID '{entityId}' was not found.")
{
    public string EntityType { get; } = entityType ?? throw new ArgumentNullException(nameof(entityType));
    public object EntityId { get; } = entityId ?? throw new ArgumentNullException(nameof(entityId));

    public static EntityNotFoundException For<T>(object id) => new(typeof(T).Name, id);
}

/// <summary>
/// Exception thrown when a business rule is violated.
/// </summary>
public class BusinessRuleViolationException : DomainException
{
    public BusinessRuleViolationException(string message)
        : base("BUSINESS_RULE_VIOLATION", message)
    {
    }

    public BusinessRuleViolationException(string code, string message)
        : base(code, message)
    {
    }
}

/// <summary>
/// Exception thrown when there's a concurrency conflict.
/// </summary>
public class ConcurrencyException : DomainException
{
    public ConcurrencyException(string entityType, object entityId)
        : base("CONCURRENCY_CONFLICT", 
               $"A concurrency conflict occurred while updating {entityType} with ID '{entityId}'.")
    {
    }
}

/// <summary>
/// Exception thrown when access is denied.
/// </summary>
public class AccessDeniedException : DomainException
{
    public AccessDeniedException(string resource)
        : base("ACCESS_DENIED", $"Access to {resource} is denied.")
    {
    }

    public AccessDeniedException(string resource, string reason)
        : base("ACCESS_DENIED", $"Access to {resource} is denied. Reason: {reason}")
    {
    }
}
