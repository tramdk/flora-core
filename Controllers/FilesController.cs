using AutoMapper;
using FloraCore.Application.Common.Interfaces;
using FloraCore.Application.Common.Models;
using FloraCore.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using Asp.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace FloraCore.Controllers;

[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class FilesController(IFileService fileService, IMapper mapper) : ControllerBase
{
    private readonly IFileService _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
    private readonly IMapper _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));

    [HttpPost("upload")]
    public async Task<ActionResult<FileResponse>> UploadFile(IFormFile file, [FromForm] string? objectId, [FromForm] string? objectType, [FromForm] bool isPublic = true)
    {
        var result = await _fileService.UploadFileAsync(file, objectId, objectType, isPublic);
        return Ok(_mapper.Map<FileResponse>(result));
    }

    [AllowAnonymous]
    [HttpPost("metadata")]
    public async Task<ActionResult<List<FileResponse>>> GetFileMetadataByObjectId([FromForm] string? objectId)
    {
        if (string.IsNullOrEmpty(objectId))
        {
            return BadRequest("ObjectId is required.");
        }

        var results = await _fileService.GetFilesByObjectIdAsync(objectId);
        if (results.Count == 0) return NotFound("File not found");
        
        return Ok(_mapper.Map<List<FileResponse>>(results));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteFile(Guid id)
    {
        var result = await _fileService.DeleteFileAsync(id);
        if (!result) return NotFound();
        return NoContent();
    }

    [AllowAnonymous]
    [HttpGet("download/{id}")]
    public async Task<IActionResult> DownloadFile(Guid id)
    {
        // ... Logic download giữ nguyên vì trả về FileResult, không phải DTO
        // Có thể refactor thành service trả về Stream/Bytes
        try
        {
            var (bytes, contentType, fileName) = await _fileService.DownloadFileAsync(id);
            return File(bytes, contentType, fileName);
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
    }

    [AllowAnonymous]
    [HttpGet("view/{id}")]
    public async Task<IActionResult> ViewFile(string id)
    {
        if (!Guid.TryParse(id, out var guidId))
        {
             return NotFound(); // Prevent ORB: No JSON body
        }

        try
        {
            var (bytes, contentType, _) = await _fileService.DownloadFileAsync(guidId);
            return File(bytes, contentType);
        }
        catch (FileNotFoundException)
        {
            // Log.Warning(ex, "File not found: {Id}", id);
            return NotFound(); // Prevent ORB: No JSON body
        }
        catch(Exception)
        {
            return NotFound(); // fallback
        }
    }

    [AllowAnonymous]
    [HttpGet("view/object/{objectId}")]
    public async Task<IActionResult> ViewFileByObjectId(string objectId)
    {
        try
        {
            var (bytes, contentType, _) = await _fileService.DownloadFileByObjectIdAsync(objectId);
            return File(bytes, contentType);
        }
        catch (FileNotFoundException)
        {
            // Log.Warning(ex, "File by objectId not found: {ObjectId}", objectId);
            return NotFound(); // Prevent ORB: No JSON body
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error viewing file by objectId: {ObjectId}", objectId);
            return NotFound(); // Prevent ORB: No JSON body
        }
    }

    [AllowAnonymous]
    [HttpGet("download/object/{objectId}")]
    public async Task<IActionResult> DownloadFileByObjectId(string objectId)
    {
        try
        {
            var (bytes, contentType, fileName) = await _fileService.DownloadFileByObjectIdAsync(objectId);
            return File(bytes, contentType, fileName);
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
    }
}
