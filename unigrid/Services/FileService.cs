using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using unigrid.Data.Repositories;
using unigrid.Models;

namespace unigrid.Services;

public class FileService : IFileService
{
    private readonly IWorkspaceRepository _workspaceRepo;
    private readonly IMemberRepository _memberRepo;
    private readonly IFileRepository _fileRepo;
    private readonly IWorkspaceService _workspaceService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<FileService> _logger;

    public FileService(
        IWorkspaceRepository workspaceRepo,
        IMemberRepository memberRepo,
        IFileRepository fileRepo,
        IWorkspaceService workspaceService,
        IUnitOfWork unitOfWork,
        ILogger<FileService> logger)
    {
        _workspaceRepo = workspaceRepo;
        _memberRepo = memberRepo;
        _fileRepo = fileRepo;
        _workspaceService = workspaceService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<(WorkspaceFile? file, string? error)> UploadFileAsync(Guid workspaceId, Guid userId, IFormFile uploadedFile, Guid? taskId = null)
    {
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        if (workspace == null) return (null, "Workspace not found.");

        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        var userRecord = members.FirstOrDefault(m => m.UserId == userId);
        if (workspace.OwnerId != userId && userRecord == null)
        {
            return (null, "Access denied to this workspace.");
        }
        string userRole = userRecord?.Role ?? "Manager";

        if (userRole == "Viewer") return (null, "Viewer role cannot upload files.");

        if (uploadedFile == null || uploadedFile.Length == 0) return (null, "No file was selected for upload.");

<<<<<<< HEAD
=======
        // 10MB per-file limit for task attachments (specification files only)
        // Workspace storage uploads are not subject to this limit
        if (taskId != null)
        {
            const long maxTaskFileSize = 10L * 1024 * 1024;
            if (uploadedFile.Length > maxTaskFileSize)
            {
                return (null, "Task attachment size exceeds the 10 MB limit. Task files should be specification documents, not large project folders.");
            }
        }

>>>>>>> da388596d2baad13bd7723b6a42b2048d2b19e49
        string originalFileName = uploadedFile.FileName;
        string baseName = Path.GetFileNameWithoutExtension(originalFileName);
        string extension = Path.GetExtension(originalFileName).TrimStart('.').ToLower();

        if (string.IsNullOrEmpty(baseName)) return (null, "Invalid file name.");

        if (baseName.Contains('.'))
        {
            return (null, "File name contains invalid characters. Multiple dots ('.') are not allowed in the file name.");
        }

        foreach (char c in baseName)
        {
            if (!char.IsLetterOrDigit(c) && c != ' ' && c != '-' && c != '_')
            {
                return (null, "File name contains invalid characters. Only alphanumeric characters, spaces, hyphens, and underscores are allowed.");
            }
        }

        string fileType = "doc";
        if (extension == "pdf") fileType = "pdf";
        else if (extension == "xls" || extension == "xlsx" || extension == "csv") fileType = "spreadsheet";
        else if (extension == "png" || extension == "jpg" || extension == "jpeg" || extension == "gif" || extension == "svg") fileType = "image";

        long maxStorageLimit = 0;
        string packageTier = workspace.PackageTier ?? "Free";
        bool isIndividualStorage = (packageTier == "Personal");

        var user = await _memberRepo.GetUserByIdAsync(userId);
        if (user == null) return (null, "User profile not found.");

        if (isIndividualStorage)
        {
            string userTier = user.SubscriptionTier ?? "Free";
            if (userTier == "Personal") maxStorageLimit = 2L * 1024 * 1024 * 1024;
            else if (userTier == "Pro") maxStorageLimit = 20L * 1024 * 1024 * 1024;
            else if (userTier == "ProPlus") maxStorageLimit = 40L * 1024 * 1024 * 1024;
            else if (userTier == "Business") maxStorageLimit = 80L * 1024 * 1024 * 1024;
            else maxStorageLimit = 0;
        }
        else
        {
            if (packageTier == "Pro") maxStorageLimit = 20L * 1024 * 1024 * 1024;
            else if (packageTier == "ProPlus") maxStorageLimit = 40L * 1024 * 1024 * 1024;
            else if (packageTier == "Business") maxStorageLimit = 80L * 1024 * 1024 * 1024;
        }

        if (packageTier == "Free")
        {
            return (null, "File uploads are not allowed on the Free plan. Please upgrade your workspace package to upload files.");
        }

        if (isIndividualStorage && maxStorageLimit <= 0)
        {
            return (null, "You do not have individual storage upload privileges in this workspace. Upgrade to a Personal plan to upload files.");
        }

        long totalStorageUsed = 0;
        if (isIndividualStorage)
        {
            totalStorageUsed = await _fileRepo.GetUserStorageUsedAsync(workspaceId, userId);
        }
        else
        {
            totalStorageUsed = await _fileRepo.GetWorkspaceStorageUsedAsync(workspaceId);
        }

        if (totalStorageUsed + uploadedFile.Length > maxStorageLimit)
        {
            string limitStr = maxStorageLimit >= 1024L * 1024 * 1024 
                ? $"{(maxStorageLimit / (1024L * 1024 * 1024))} GB" 
                : "0 GB";
            string typeStr = isIndividualStorage ? "individual" : "workspace";
            return (null, $"Upload failed. You have exceeded your {typeStr} storage limit of {limitStr} for the {packageTier} plan.");
        }

        // Save physical file safely
        string uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "files", workspaceId.ToString());
        if (!Directory.Exists(uploadDir))
        {
            Directory.CreateDirectory(uploadDir);
        }

        string safeFileName = originalFileName.ToLower().Replace(" ", "_");
        string physicalPath = Path.Combine(uploadDir, safeFileName);

        using (var stream = new FileStream(physicalPath, FileMode.Create))
        {
            await uploadedFile.CopyToAsync(stream);
        }

        var file = new WorkspaceFile
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            TaskId = taskId,
            UserId = userId,
            FileName = originalFileName,
            FileUrl = $"files/{workspaceId}/{safeFileName}",
            FileType = fileType,
            FileSize = uploadedFile.Length,
            IsPublic = true,
            CreatedAt = DateTime.UtcNow
        };

        await _fileRepo.AddAsync(file);
        await _unitOfWork.SaveChangesAsync();

        _workspaceService.EvictWorkspaceCache(workspaceId, members.Select(m => m.UserId).ToList());
        _logger.LogInformation("File uploaded: {FileName} in workspace {WorkspaceId} by user {UserId}", file.FileName, workspaceId, userId);

        return (file, null);
    }

    public async Task<string?> DeleteFileAsync(Guid workspaceId, Guid userId, Guid fileId)
    {
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        if (workspace == null) return "Workspace not found.";

        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        var userRecord = members.FirstOrDefault(m => m.UserId == userId);
        if (workspace.OwnerId != userId && userRecord == null)
        {
            return "Access denied to this workspace.";
        }
        string userRole = userRecord?.Role ?? "Manager";

        bool canDelete = IsMemberAllowed(workspace, members, userId, "disabledDeleteFileUsers", userRole);
        if (!canDelete) return "You do not have permission to delete files.";

        var file = await _fileRepo.GetByIdAsync(fileId);
        if (file == null || file.WorkspaceId != workspaceId) return "File not found.";

        // Delete physical file safely
        string physicalPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", file.FileUrl);
        if (File.Exists(physicalPath))
        {
            try
            {
                File.Delete(physicalPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to physically delete file: {Path}", physicalPath);
            }
        }

        _fileRepo.Remove(file);
        await _unitOfWork.SaveChangesAsync();

        _workspaceService.EvictWorkspaceCache(workspaceId, members.Select(m => m.UserId).ToList());
        _logger.LogInformation("File deleted: {FileName} from Workspace {WorkspaceId} by user {UserId}", file.FileName, workspaceId, userId);

        return null; // Success
    }

    private bool IsMemberAllowed(Workspace workspace, List<WorkspaceMember> members, Guid memberId, string key, string role)
    {
        if (workspace == null) return true;
        if (workspace.OwnerId == memberId) return true;

        var memberRecord = members.FirstOrDefault(m => m.UserId == memberId);
        if (memberRecord != null && memberRecord.Role == "Manager")
        {
            return true;
        }

        bool defaultAllowed = (role != "Viewer");

        if (!string.IsNullOrEmpty(workspace.SettingsJson))
        {
            try
            {
                var payload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(workspace.SettingsJson);
                if (payload != null && payload[key] != null)
                {
                    var disabledList = System.Text.Json.JsonSerializer.Deserialize<List<string>>(payload[key].ToJsonString());
                    if (disabledList != null)
                    {
                        return !disabledList.Contains(memberId.ToString().ToLower());
                    }
                }
            }
            catch {}
        }

        return defaultAllowed;
    }
}
