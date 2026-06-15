using AutoMapper;
using FloraCore.Application.Common.Models;
using FloraCore.Application.Features.Posts.DTOs;
using FloraCore.Application.Features.Products.DTOs;
using FloraCore.Application.Features.Users.Queries;
using FloraCore.Application.Features.Users.DTOs;
using FloraCore.Application.Features.Users.DTOs;
using FloraCore.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace FloraCore.Application.Common.Mappings;

public class MappingProfile : Profile
{
    public /* bypass-static-check */ MappingProfile()
    {
        // Files
        CreateMap<FileMetadata, FileResponse>()
            .ForMember(dest => dest.ViewUrl, opt => opt.MapFrom<FileViewUrlResolver>())
            .ForMember(dest => dest.DownloadUrl, opt => opt.MapFrom<FileDownloadUrlResolver>());

        // Posts
        CreateMap<Post, PostDetailDto>()
            .ForMember(dest => dest.AuthorName, opt => opt.MapFrom(src => src.Author != null ? src.Author.FullName : null));
            
        CreateMap<Post, PostDto>()
            .ForMember(dest => dest.AuthorName, opt => opt.MapFrom(src => src.Author != null ? src.Author.FullName : null));

        // Products
        CreateMap<Product, ProductDto>()
            .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.Category != null ? src.Category.Name : null));
        
        CreateMap<ProductReview, ReviewDto>()
            .ForMember(dest => dest.UserName, opt => opt.MapFrom(src => src.User != null ? src.User.FullName : "Anonymous"));

        // Users
        CreateMap<AppUser, UserDto>()
            .ForMember(dest => dest.Roles, opt => opt.Ignore()); // Roles handled manually in handler usually
    }
}

public class FileViewUrlResolver : IValueResolver<FileMetadata, FileResponse, string>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FileViewUrlResolver(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string Resolve(FileMetadata source, FileResponse destination, string destMember, ResolutionContext context)
    {
        if (!string.IsNullOrEmpty(source.Url))
        {
            return source.Url;
        }

        var request = _httpContextAccessor.HttpContext?.Request;
        if (request == null) return string.Empty;

        var baseUrl = $"{request.Scheme}://{request.Host}{request.PathBase}/api/Files";
        return $"{baseUrl}/view/object/{source.ObjectId}";
    }
}

public class FileDownloadUrlResolver : IValueResolver<FileMetadata, FileResponse, string>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FileDownloadUrlResolver(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string Resolve(FileMetadata source, FileResponse destination, string destMember, ResolutionContext context)
    {
        if (!string.IsNullOrEmpty(source.Url))
        {
            return source.Url;
        }

        var request = _httpContextAccessor.HttpContext?.Request;
        if (request == null) return string.Empty;

        var baseUrl = $"{request.Scheme}://{request.Host}{request.PathBase}/api/Files";
        return $"{baseUrl}/download/object/{source.ObjectId}";
    }
}
