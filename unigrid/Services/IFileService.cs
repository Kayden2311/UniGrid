using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using unigrid.Models;

namespace unigrid.Services;

public interface IFileService
{
    Task<(WorkspaceFile? file, string? error)> UploadFileAsync(Guid workspaceId, Guid userId, IFormFile uploadedFile, Guid? taskId = null);
    Task<string?> DeleteFileAsync(Guid workspaceId, Guid userId, Guid fileId);
}
