using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using unigrid.Data;
using unigrid.Models;

namespace unigrid.Pages
{
    [Authorize(Roles = "2")] // Restrict to authenticated Users
    public class ProfileModel : PageModel
    {
        private readonly UniGridDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ProfileModel> _logger;

        public ProfileModel(UniGridDbContext context, IMemoryCache cache, ILogger<ProfileModel> logger)
        {
            _context = context;
            _cache = cache;
            _logger = logger;
        }

        [BindProperty]
        public string FullName { get; set; } = string.Empty;

        [BindProperty]
        public string? AvatarUrl { get; set; }

        [BindProperty]
        public IFormFile? AvatarFile { get; set; }

        [BindProperty]
        public bool RemoveAvatar { get; set; }

        public string Email { get; set; } = string.Empty;
        public bool IsGoogleConnected { get; set; }
        public string Initials { get; set; } = "U";
        public bool HasNoProfile { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var accountIdClaim = User.FindFirst("AccountId")?.Value;
            if (string.IsNullOrEmpty(accountIdClaim)) return RedirectToPage("/Login");

            var accountId = Guid.Parse(accountIdClaim);
            var user = await _context.Users
                .Include(u => u.Account)
                .FirstOrDefaultAsync(u => u.AccountId == accountId);

            if (user == null)
            {
                HasNoProfile = true;
                FullName = "";
                AvatarUrl = "";
                
                var accountRecord = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);
                Email = accountRecord?.Email ?? "";
                IsGoogleConnected = accountRecord?.PasswordHash == "GOOGLE_OAUTH";
                Initials = "U";
                
                ViewData["Workspaces"] = new List<Workspace>();
                ViewData["UserName"] = "New User";
                ViewData["UserInitials"] = "U";
                
                return Page();
            }

            HasNoProfile = false;
            FullName = user.FullName;
            AvatarUrl = user.AvatarUrl;
            Email = user.Account.Email;
            IsGoogleConnected = user.Account.PasswordHash == "GOOGLE_OAUTH";
            
            Initials = string.Concat(user.FullName.Split(' ').Select(n => n[0]));

            // Load workspaces for sidebar
            var userWorkspaces = await _cache.GetOrCreateAsync($"UserWorkspaces_{user.Id}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                return await _context.Workspaces
                    .Where(w => !w.IsDisabled && (w.OwnerId == user.Id || w.WorkspaceMembers.Any(m => !m.IsDisabled && m.UserId == user.Id)))
                    .ToListAsync();
            });
            ViewData["Workspaces"] = userWorkspaces;
            ViewData["UserName"] = user.FullName;
            ViewData["UserInitials"] = Initials;

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var accountIdClaim = User.FindFirst("AccountId")?.Value;
            if (string.IsNullOrEmpty(accountIdClaim)) return RedirectToPage("/Login");

            var accountId = Guid.Parse(accountIdClaim);
            var user = await _context.Users
                .Include(u => u.Account)
                .FirstOrDefaultAsync(u => u.AccountId == accountId);

            // Handle Avatar Upload or Removal
            if (AvatarFile != null && AvatarFile.Length > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                var extension = Path.GetExtension(AvatarFile.FileName).ToLower();
                if (!allowedExtensions.Contains(extension))
                {
                    ModelState.AddModelError(nameof(AvatarFile), "Invalid image file type. Only JPG, PNG, GIF, and WEBP are allowed.");
                }
                else if (AvatarFile.Length > 5 * 1024 * 1024)
                {
                    ModelState.AddModelError(nameof(AvatarFile), "Image file size exceeds the 5MB limit.");
                }
                else
                {
                    var uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "avatars");
                    if (!Directory.Exists(uploadDir))
                    {
                        Directory.CreateDirectory(uploadDir);
                    }

                    // Delete old local avatar file
                    if (user != null && !string.IsNullOrEmpty(user.AvatarUrl) && user.AvatarUrl.StartsWith("/uploads/avatars/"))
                    {
                        var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", user.AvatarUrl.TrimStart('/'));
                        if (System.IO.File.Exists(oldFilePath))
                        {
                            try { System.IO.File.Delete(oldFilePath); } catch (Exception ex) { _logger.LogError(ex, "Failed to delete old file"); }
                        }
                    }

                    var uniqueFileName = $"{Guid.NewGuid()}{extension}";
                    var filePath = Path.Combine(uploadDir, uniqueFileName);
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await AvatarFile.CopyToAsync(stream);
                    }
                    AvatarUrl = $"/uploads/avatars/{uniqueFileName}";
                }
            }
            else if (RemoveAvatar)
            {
                // Delete old local avatar file
                if (user != null && !string.IsNullOrEmpty(user.AvatarUrl) && user.AvatarUrl.StartsWith("/uploads/avatars/"))
                {
                    var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", user.AvatarUrl.TrimStart('/'));
                    if (System.IO.File.Exists(oldFilePath))
                    {
                        try { System.IO.File.Delete(oldFilePath); } catch (Exception ex) { _logger.LogError(ex, "Failed to delete old file"); }
                    }
                }
                AvatarUrl = null;
            }
            else
            {
                AvatarUrl = user?.AvatarUrl;
            }

            if (string.IsNullOrWhiteSpace(FullName))
            {
                ModelState.AddModelError(nameof(FullName), "Full Name cannot be empty.");
            }

            if (!ModelState.IsValid)
            {
                var accountRecord = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);
                Email = accountRecord?.Email ?? "";
                IsGoogleConnected = accountRecord?.PasswordHash == "GOOGLE_OAUTH";
                Initials = user != null ? string.Concat(user.FullName.Split(' ').Select(n => n[0])) : "U";
                HasNoProfile = user == null;
                
                var userWorkspaces = user != null ? await _cache.GetOrCreateAsync($"UserWorkspaces_{user.Id}", async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                    return await _context.Workspaces
                        .Where(w => !w.IsDisabled && (w.OwnerId == user.Id || w.WorkspaceMembers.Any(m => !m.IsDisabled && m.UserId == user.Id)))
                        .ToListAsync();
                }) : new List<Workspace>();

                ViewData["Workspaces"] = userWorkspaces;
                ViewData["UserName"] = user?.FullName ?? "New User";
                ViewData["UserInitials"] = Initials;
                return Page();
            }

            if (user == null)
            {
                // Create user profile
                user = new User
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    FullName = Helpers.InputSanitizer.SanitizeInput(FullName.Trim()),
                    SubscriptionTier = "Free",
                    AvatarUrl = AvatarUrl
                };
                await _context.Users.AddAsync(user);
                await _context.SaveChangesAsync();
                
                _cache.Remove($"User_{accountId}");
            }
            else
            {
                // Update user details
                user.FullName = Helpers.InputSanitizer.SanitizeInput(FullName.Trim());
                user.AvatarUrl = AvatarUrl;
                await _context.SaveChangesAsync();
                
                _cache.Remove($"User_{accountId}");
                _cache.Remove($"UserWorkspaces_{user.Id}");
            }

            // Dynamic claim update in the active session cookie
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Account.Email),
                new Claim("FullName", user.FullName),
                new Claim(ClaimTypes.Role, user.Account.Role.ToString()),
                new Claim("AccountId", user.Account.Id.ToString())
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            TempData["ProfileSuccess"] = "Account profile updated successfully!";
            _logger.LogInformation("Profile updated/created for user account {AccountId}", accountId);

            return RedirectToPage("/Dashboard");
        }
    }
}
