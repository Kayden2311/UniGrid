using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using unigrid.Data;
using unigrid.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace unigrid.Pages.Admin
{
    [Authorize(Roles = "1")]
    public class UsersModel : PageModel
    {
        private readonly UniGridDbContext _context;

        public UsersModel(UniGridDbContext context)
        {
            _context = context;
        }

        // Filters
        [BindProperty(SupportsGet = true)]
        public string? SearchQuery { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? RoleFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool? LockedFilter { get; set; }

        // Pagination
        [BindProperty(SupportsGet = true)]
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; } = 15;
        public int TotalPages { get; set; }
        public int TotalCount { get; set; }

        public class UserViewModel
        {
            public Guid AccountId { get; set; }
            public string Email { get; set; } = null!;
            public int Role { get; set; }
            public bool IsLocked { get; set; }
            public DateTime CreatedAt { get; set; }
            public string FullName { get; set; } = "Unknown";
        }

        public List<UserViewModel> AccountsList { get; set; } = new();

        public async System.Threading.Tasks.Task OnGetAsync()
        {
            var query = _context.Accounts
                .Include(a => a.Users)
                .Include(a => a.Admins)
                .Include(a => a.Moderators)
                .AsQueryable();

            // Apply Filters
            if (!string.IsNullOrEmpty(SearchQuery))
            {
                var search = SearchQuery.ToLower();
                // Match email, or full name in profile tables
                query = query.Where(a => 
                    a.Email.ToLower().Contains(search) ||
                    a.Users.Any(u => u.FullName.ToLower().Contains(search)) ||
                    a.Admins.Any(ad => ad.FullName.ToLower().Contains(search)) ||
                    a.Moderators.Any(m => m.FullName.ToLower().Contains(search))
                );
            }

            if (RoleFilter.HasValue)
            {
                query = query.Where(a => a.Role == RoleFilter.Value);
            }

            if (LockedFilter.HasValue)
            {
                query = query.Where(a => (a.IsLocked == true) == LockedFilter.Value);
            }

            // Calculate Counts for Pagination
            TotalCount = await query.CountAsync();
            TotalPages = (int)Math.Ceiling((double)TotalCount / PageSize);
            if (CurrentPage < 1) CurrentPage = 1;
            if (TotalPages > 0 && CurrentPage > TotalPages) CurrentPage = TotalPages;

            // Fetch Paginated Results
            var results = await query
                .OrderByDescending(a => a.CreatedAt)
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            // Map to ViewModel
            AccountsList = results.Select(a => new UserViewModel
            {
                AccountId = a.Id,
                Email = a.Email,
                Role = a.Role,
                IsLocked = a.IsLocked ?? false,
                CreatedAt = a.CreatedAt ?? DateTime.UtcNow,
                FullName = a.Role switch
                {
                    1 => a.Admins.FirstOrDefault()?.FullName ?? "Admin User",
                    2 => a.Users.FirstOrDefault()?.FullName ?? "Member User",
                    3 => a.Moderators.FirstOrDefault()?.FullName ?? "Moderator",
                    _ => "Unknown"
                }
            }).ToList();
        }

        // Toggle Account Lock Status
        public async System.Threading.Tasks.Task<IActionResult> OnPostToggleLockAsync(Guid accountId)
        {
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);
            if (account == null) return NotFound();

            // Prevent self-lockout
            var adminIdClaim = User.FindFirst("AccountId")?.Value;
            if (adminIdClaim != null && Guid.TryParse(adminIdClaim, out var currentAdminId))
            {
                if (currentAdminId == accountId)
                {
                    TempData["UserError"] = "Operation Denied: You cannot lock your own administrator account.";
                    return RedirectToPage();
                }
            }

            account.IsLocked = !(account.IsLocked ?? false);
            await _context.SaveChangesAsync();

            TempData["UserSuccess"] = $"Account status for {account.Email} has been successfully updated.";
            return RedirectToPage();
        }

        // Change Account System Role
        public async System.Threading.Tasks.Task<IActionResult> OnPostChangeRoleAsync(Guid accountId, int newRole)
        {
            if (newRole < 1 || newRole > 3)
            {
                TempData["UserError"] = "Invalid role selection.";
                return RedirectToPage();
            }

            var account = await _context.Accounts
                .Include(a => a.Users)
                .Include(a => a.Admins)
                .Include(a => a.Moderators)
                .FirstOrDefaultAsync(a => a.Id == accountId);

            if (account == null) return NotFound();

            // Prevent self-role modification (accidental loss of admin access)
            var adminIdClaim = User.FindFirst("AccountId")?.Value;
            if (adminIdClaim != null && Guid.TryParse(adminIdClaim, out var currentAdminId))
            {
                if (currentAdminId == accountId && newRole != 1)
                {
                    TempData["UserError"] = "Operation Denied: You cannot downgrade your own administrator account role.";
                    return RedirectToPage();
                }
            }

            // Move profiles if necessary
            // If going to Admin (1)
            if (newRole == 1 && !account.Admins.Any())
            {
                var name = account.Users.FirstOrDefault()?.FullName ?? account.Moderators.FirstOrDefault()?.FullName ?? "New Admin";
                var adminProfile = new unigrid.Models.Admin { AccountId = account.Id, FullName = name, SuperAdmin = false };
                await _context.Admins.AddAsync(adminProfile);
            }
            // If going to User (2)
            else if (newRole == 2 && !account.Users.Any())
            {
                var name = account.Admins.FirstOrDefault()?.FullName ?? account.Moderators.FirstOrDefault()?.FullName ?? "New User";
                var userProfile = new User { AccountId = account.Id, FullName = name, SubscriptionTier = "Free" };
                await _context.Users.AddAsync(userProfile);
            }
            // If going to Moderator (3)
            else if (newRole == 3 && !account.Moderators.Any())
            {
                var name = account.Admins.FirstOrDefault()?.FullName ?? account.Users.FirstOrDefault()?.FullName ?? "New Moderator";
                var modProfile = new Moderator { AccountId = account.Id, FullName = name, Region = "Global" };
                await _context.Moderators.AddAsync(modProfile);
            }

            account.Role = newRole;
            await _context.SaveChangesAsync();

            TempData["UserSuccess"] = $"Role for {account.Email} changed to {(newRole == 1 ? "Admin" : newRole == 2 ? "User" : "Moderator")}.";
            return RedirectToPage();
        }
    }
}
