using FloraCore.Application.Common.Interfaces;
using FloraCore.Application.Common.Models;
using FloraCore.Domain.Entities;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Polly;
using Polly.Registry;
using FloraCore.Domain.Exceptions;

namespace FloraCore.Infrastructure.Services;

/// <summary>
/// Implementation of IFileService using Cloudinary for file storage.
/// </summary>
public class CloudinaryFileService(
    IGenericRepository<FileMetadata, Guid> repository,
    IOptions<CloudinarySettings> config,
    ICurrentUserService currentUserService,
    IHttpClientFactory httpClientFactory,
    ResiliencePipelineProvider<string> pipelineProvider,
        IResourceManager resourceManager) : IFileService
{
    private readonly IGenericRepository<FileMetadata, Guid> _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly Cloudinary _cloudinary = new Cloudinary(new Account((config ?? throw new ArgumentNullException(nameof(config))).Value.CloudName, config.Value.ApiKey, config.Value.ApiSecret));
    private readonly ICurrentUserService _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    private readonly HttpClient _httpClient = (httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory))).CreateClient("ResilientClient");
    private readonly ResiliencePipeline _resiliencePipeline = (pipelineProvider ?? throw new ArgumentNullException(nameof(pipelineProvider))).GetPipeline("external-services");
    private readonly IResourceManager _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
    private readonly IOptions<CloudinarySettings> _config = config;

    /// <summary>
    /// Uploads a file to Cloudinary.
    /// </summary>
    /// <param name="file">The file to upload.</param>
    /// <param name="objectId">The ID of the related object (optional).</param>
    /// <param name="objectType">The type of the related object (optional).</param>
    /// <param name="isPublic">Whether the file is public (optional).</param>
    /// <returns>The file metadata.</returns>
    /// <exception cref="ArgumentException">Thrown if the file is empty or objectId/objectType is missing.</exception>
    /// <exception cref="Exception">Thrown if Cloudinary upload fails.</exception>
    public async Task<FileMetadata> UploadFileAsync(IFormFile file, string? objectId = null, string? objectType = null, bool isPublic = true)
    {
        if (file == null || file.Length == 0) throw new ArgumentException("File is empty", nameof(file));
        if (string.IsNullOrEmpty(objectId)) throw new ArgumentException("objectId is empty", nameof(objectId));
        if (string.IsNullOrEmpty(objectType)) throw new ArgumentException("objectType is empty", nameof(objectType));

        var uploadResult = new RawUploadResult();
        using (var stream = file.OpenReadStream())
        {
            var uploadParams = new RawUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = _config.Value.UploadFolder,
                PublicId = $"{Guid.NewGuid()}_{Path.GetFileNameWithoutExtension(file.FileName)}"
            };

            uploadResult = await _resiliencePipeline.ExecuteAsync(async ct =>
                await _cloudinary.UploadAsync(uploadParams));
        }

        if (uploadResult.Error != null)
            throw new Exception(uploadResult.Error.Message);

        var metadata = new FileMetadata
        {
            Id = Guid.NewGuid(),
            FileName = file.FileName,
            StoredName = uploadResult.PublicId,
            FilePath = uploadResult.SecureUrl.ToString(),
            PublicId = uploadResult.PublicId,
            Url = uploadResult.SecureUrl.ToString(),
            ContentType = file.ContentType,
            FileSize = file.Length,
            UploadedAt = DateTime.UtcNow,
            ObjectId = objectId,
            ObjectType = objectType,
            UploadedById = _currentUserService.UserId,
            IsPublic = isPublic
        };

        await _repository.AddAsync(metadata);
        return metadata;
    }

    /// <summary>
    /// Gets files by object ID and type.
    /// </summary>
    /// <param name="objectId">The object ID.</param>
    /// <param name="objectType">The object type.</param>
    /// <returns>The list of file metadata.</returns>
    public async Task<IEnumerable<FileMetadata>> GetFilesByObjectAsync(string objectId, string objectType)
    {
        var currentUserId = _currentUserService.UserId;
        return await _repository.FindAsync(f => f.ObjectId == objectId && f.ObjectType == objectType && (f.IsPublic || f.UploadedById == currentUserId));
    }

    /// <summary>
    /// Gets files by object ID.
    /// </summary>
    /// <param name="objectId">The object ID.</param>
    /// <returns>The list of file metadata.</returns>
    public async Task<List<FileMetadata>> GetFilesByObjectIdAsync(string objectId)
    {
        var currentUserId = _currentUserService.UserId;
        var query = _repository.GetQueryable();

        if (Guid.TryParse(objectId, out var idAsGuid))
        {
            return await query
                .Where(f => (f.ObjectId == objectId || f.Id == idAsGuid) && (f.IsPublic || f.UploadedById == currentUserId))
                .ToListAsync();
        }

        return await query
            .Where(f => f.ObjectId == objectId && (f.IsPublic || f.UploadedById == currentUserId))
            .ToListAsync();
    }

    /// <summary>
    /// Deletes a file from Cloudinary.
    /// </summary>
    /// <param name="fileId">The ID of the file to delete.</param>
    /// <returns>True if the file was deleted successfully, otherwise false.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if the user is not authorized to delete the file.</exception>
    public async Task<bool> DeleteFileAsync(Guid fileId)
    {
        var metadata = await _repository.GetByIdAsync(fileId);
        if (metadata == null) return false;

        if (metadata.UploadedById != _currentUserService.UserId)
            throw new UnauthorizedAccessException(_resourceManager.GetString("UnauthorizedToDeleteFile"));

        try
        {
            var deletionParams = new DeletionParams(metadata.PublicId ?? metadata.StoredName);
            await _resiliencePipeline.ExecuteAsync(async ct =>
                await _cloudinary.DestroyAsync(deletionParams));
        }
        catch (Exception)
        {
            // Log warning or info: physical file deletion failed or already deleted
        }

        await _repository.DeleteAsync(metadata);
        return true;
    }

    /// <summary>
    /// Downloads a file from Cloudinary.
    /// </summary>
    /// <param name="fileId">The ID of the file to download.</param>
    /// <returns>The file bytes, content type, and file name.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the file metadata is not found.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown if the user is not authorized to access the file.</exception>
    public async Task<(byte[] Bytes, string ContentType, string FileName)> DownloadFileAsync(Guid fileId)
    {
        var metadata = await _repository.GetByIdAsync(fileId);
        if (metadata == null) throw new FileNotFoundException(_resourceManager.GetString("FileNotFound"));

        if (!metadata.IsPublic && metadata.UploadedById != _currentUserService.UserId)
            throw new UnauthorizedAccessException(_resourceManager.GetString("UnauthorizedToAccessPrivateFile"));

        var fileUrl = metadata.Url ?? metadata.FilePath;
        byte[] bytes;
        try
        {
            bytes = await _httpClient.GetByteArrayAsync(fileUrl);
        }
        catch (HttpRequestException ex)
        {
            throw new FileNotFoundException(_resourceManager.GetString("FileNotFound") + " URL: " + fileUrl, ex);
        }

        return (bytes, metadata.ContentType, metadata.FileName);
    }

    /// <summary>
    /// Downloads a file from Cloudinary by object ID.
    /// </summary>
    /// <param name="objectId">The object ID.</param>
    /// <returns>The file bytes, content type, and file name.</returns>
    /// <exception cref="FileNotFoundException">Thrown if no database entry is found for the object ID.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown if the user is not authorized to access the file.</exception>
    public async Task<(byte[] Bytes, string ContentType, string FileName)> DownloadFileByObjectIdAsync(string objectId)
    {
        var files = await _repository.FindAsync(f => f.ObjectId == objectId);
        var metadata = files.OrderByDescending(f => f.UploadedAt).FirstOrDefault();

        if (metadata == null)
            throw new FileNotFoundException(string.Format(_resourceManager.GetString("NoDatabaseEntryForObjectId"), objectId));

        if (!metadata.IsPublic && metadata.UploadedById != _currentUserService.UserId)
            throw new UnauthorizedAccessException(_resourceManager.GetString("UnauthorizedToAccessPrivateFile"));

        var fileUrl = metadata.Url ?? metadata.FilePath;
        byte[] bytes;
        try
        {
            bytes = await _httpClient.GetByteArrayAsync(fileUrl);
        }
        catch (HttpRequestException ex)
        {
            throw new FileNotFoundException(_resourceManager.GetString("FileNotFound") + " URL: " + fileUrl, ex);
        }

        return (bytes, metadata.ContentType, metadata.FileName);
    }
}
