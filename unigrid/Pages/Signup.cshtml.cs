using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using unigrid.Data;
using unigrid.Models;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace unigrid.Pages;

public class SignupModel : PageModel
{
    private readonly UniGridDbContext _context;
    private readonly ILogger<SignupModel> _logger;

    public SignupModel(UniGridDbContext context, ILogger<SignupModel> logger)
    {
        _context = context;
        _logger = logger;
    }

    [BindProperty]
    public string FullName { get; set; } = string.Empty;

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string Tier { get; set; } = "Free";

    public string ErrorMessage { get; set; } = string.Empty;

    public void OnGet()
    {
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostAsync()
    {
        _logger.LogInformation("Signup POST request received for: {Email}", Email);

        if (string.IsNullOrEmpty(FullName) || string.IsNullOrEmpty(Email) || string.IsNullOrEmpty(Password))
        {
            _logger.LogWarning("Signup failed: Missing fields.");
            ErrorMessage = "Please fill in all the required fields.";
            return Page();
        }

        if (Password.Length < 8)
        {
            _logger.LogWarning("Signup failed: Password too short.");
            ErrorMessage = "Password must be at least 8 characters long.";
            return Page();
        }

        // Check if account already exists
        var existingAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.Email == Email);
        if (existingAccount != null)
        {
            _logger.LogWarning("Signup failed: Account already exists for {Email}.", Email);
            ErrorMessage = "An account with this email address already exists.";
            return Page();
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // 1. Create the Account (Role 2 is User)
            var newAccount = new Account
            {
                Id = Guid.NewGuid(),
                Email = Email,
                PasswordHash = Password, // Stored as plain text for demo context (or matching VerifyAccount simple check)
                Role = 2,
                IsLocked = false,
                CreatedAt = DateTime.UtcNow
            };

            await _context.Accounts.AddAsync(newAccount);

            // 2. Create the User Profile
            var newUser = new User
            {
                Id = Guid.NewGuid(),
                AccountId = newAccount.Id,
                FullName = FullName,
                SubscriptionTier = string.IsNullOrEmpty(Tier) ? "Free" : Tier,
                SubscriptionExpires = DateTime.UtcNow.AddYears(1)
            };

            await _context.Users.AddAsync(newUser);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Account and user profile created successfully for {Email}.", Email);

            // 3. Automatically Sign In the user after successful signup
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, Email),
                new Claim("FullName", FullName),
                new Claim(ClaimTypes.Role, "2"),
                new Claim("AccountId", newAccount.Id.ToString())
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

            _logger.LogInformation("Redirecting newly signed-up user to Dashboard.");
            return RedirectToPage("/Dashboard");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error creating account during signup flow.");
            ErrorMessage = "An error occurred while creating your account. Please try again.";
            return Page();
        }
    }
}
