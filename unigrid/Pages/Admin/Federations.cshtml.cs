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
    public class FederationsModel : PageModel
    {
        private readonly UniGridDbContext _context;

        public FederationsModel(UniGridDbContext context)
        {
            _context = context;
        }

        public class FederationViewModel
        {
            public Guid FederationId { get; set; }
            public string Name { get; set; } = null!;
            public string JoinCode { get; set; } = null!;
            public DateTime CreatedAt { get; set; }
            public string OwnerName { get; set; } = null!;
            public string OwnerEmail { get; set; } = null!;
            public List<WorkspaceInfo> LinkedWorkspaces { get; set; } = new();
            public bool IsDisabled { get; set; }
        }

        public class WorkspaceInfo
        {
            public Guid WorkspaceId { get; set; }
            public string Name { get; set; } = null!;
            public string Type { get; set; } = null!;
            public string OwnerName { get; set; } = null!;
        }

        public List<FederationViewModel> FederationsList { get; set; } = new();

        public async System.Threading.Tasks.Task OnGetAsync()
        {
            var federations = await _context.WorkspaceFederations
                .Include(f => f.Owner)
                .Include(f => f.Workspaces)
                    .ThenInclude(w => w.Owner)
                .OrderByDescending(f => f.CreatedAt)
                .ToListAsync();

            var accounts = await _context.Accounts.ToListAsync();

            FederationsList = federations.Select(f => new FederationViewModel
            {
                FederationId = f.Id,
                Name = f.Name,
                JoinCode = f.JoinCode,
                CreatedAt = f.CreatedAt,
                OwnerName = f.Owner.FullName,
                OwnerEmail = accounts.FirstOrDefault(a => a.Id == f.Owner.AccountId)?.Email ?? "Unknown",
                LinkedWorkspaces = f.Workspaces.Select(w => new WorkspaceInfo
                {
                    WorkspaceId = w.Id,
                    Name = w.Name,
                    Type = w.WorkspaceType,
                    OwnerName = w.Owner.FullName
                }).ToList(),
                IsDisabled = f.IsDisabled
            }).ToList();
        }

        // Action: Create Federation directly
        public async System.Threading.Tasks.Task<IActionResult> OnPostCreateFederationAsync(string name, string joinCode, string ownerEmail)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(joinCode) || string.IsNullOrEmpty(ownerEmail))
            {
                TempData["FedError"] = "All fields are required to create a federation.";
                return RedirectToPage();
            }

            // Check if join code is unique
            var codeExists = await _context.WorkspaceFederations.AnyAsync(f => f.JoinCode.ToLower() == joinCode.ToLower());
            if (codeExists)
            {
                TempData["FedError"] = $"Join Code '{joinCode}' is already in use by another federation.";
                return RedirectToPage();
            }

            // Find account
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Email.ToLower() == ownerEmail.ToLower());
            if (account == null)
            {
                TempData["FedError"] = $"Account with email '{ownerEmail}' not found.";
                return RedirectToPage();
            }

            // Find user profile
            var user = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == account.Id);
            if (user == null)
            {
                TempData["FedError"] = $"User profile for account '{ownerEmail}' is missing.";
                return RedirectToPage();
            }

            // Create federation
            var federation = new WorkspaceFederation
            {
                Id = Guid.NewGuid(),
                Name = name,
                JoinCode = joinCode,
                OwnerId = user.Id,
                CreatedAt = DateTime.UtcNow
            };

            await _context.WorkspaceFederations.AddAsync(federation);
            await _context.SaveChangesAsync();

            // Self-healing check: ensure a ChatRoom is created for this federation (as SignalR communications require it)
            var room = new ChatRoom
            {
                Id = Guid.NewGuid(),
                FederationId = federation.Id,
                CreatedAt = DateTime.UtcNow
            };
            await _context.ChatRooms.AddAsync(room);
            await _context.SaveChangesAsync();

            TempData["FedSuccess"] = $"Federation '{name}' has been successfully created with owner {ownerEmail}.";
            return RedirectToPage();
        }

        // Action: Toggle Disable Federation
        public async System.Threading.Tasks.Task<IActionResult> OnPostToggleDisableAsync(Guid federationId)
        {
            var federation = await _context.WorkspaceFederations.FirstOrDefaultAsync(f => f.Id == federationId);
            if (federation == null) return NotFound();

            federation.IsDisabled = !federation.IsDisabled;
            await _context.SaveChangesAsync();

            TempData["FedSuccess"] = $"Federation '{federation.Name}' status updated successfully.";
            return RedirectToPage();
        }

        // Action: Unlink workspace node from Federation
        public async System.Threading.Tasks.Task<IActionResult> OnPostUnlinkWorkspaceAsync(Guid workspaceId)
        {
            var workspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId);
            if (workspace == null) return NotFound();

            var oldFedId = workspace.FederationId;
            workspace.FederationId = null;
            await _context.SaveChangesAsync();

            TempData["FedSuccess"] = $"Workspace '{workspace.Name}' has been unlinked from the federation.";
            return RedirectToPage();
        }
    }
}
