using System;
using System.Threading;
using System.Threading.Tasks;
using FloraCore.Application.Common.Interfaces;
using FloraCore.Domain.Entities;
using MediatR;

namespace FloraCore.Application.Features.Posts.Commands;

/// <summary>
/// Command to approve a blog post.
/// </summary>
public record ApprovePostCommand(Guid Id) : IRequest<bool>;

/// <summary>
/// Handler for <see cref="ApprovePostCommand"/>.
/// </summary>
public class ApprovePostCommandHandler : IRequestHandler<ApprovePostCommand, bool>
{
    private readonly IGenericRepository<Post, Guid> _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApprovePostCommandHandler"/> class.
    /// </summary>
    public ApprovePostCommandHandler(IGenericRepository<Post, Guid> repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc />
    public async Task<bool> Handle(ApprovePostCommand request, CancellationToken cancellationToken)
    {
        var post = await _repository.GetByIdAsync(request.Id);
        if (post == null)
        {
            return false;
        }

        post.IsApproved = true;
        await _repository.UpdateAsync(post);
        return true;
    }
}
