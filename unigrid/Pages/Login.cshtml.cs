using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using unigrid.Services;
using unigrid.Models;
using Microsoft.Extensions.Logging;

namespace unigrid.Pages;

[IgnoreAntiforgeryToken]
public class LoginModel : PageModel
{
    private readonly IAuthService _authService;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(IAuthService authService, ILogger<LoginModel> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;

    public async System.Threading.Tasks.Task OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            await HttpContext.SignOutAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostAsync()
    {
        _logger.LogInformation("Login POST request received for: {Email}", Email);

        // Always sign out any existing session first to prevent stale cookie conflicts
        await HttpContext.SignOutAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);

        if (string.IsNullOrEmpty(Email) || string.IsNullOrEmpty(Password))
        {
            _logger.LogWarning("Login failed: Missing email or password.");
            ErrorMessage = "Please enter both email and password.";
            return Page();
        }

        // Use the centralized AuthService to verify the account
        var account = await _authService.VerifyAccountAsync(Email, Password);

        if (account != null)
        {
            var fullName = "System User";
            if (account.Role == 1) fullName = account.Admins.FirstOrDefault()?.FullName ?? "Admin";
            else if (account.Role == 2) fullName = account.Users.FirstOrDefault()?.FullName ?? "User";
            else if (account.Role == 3) fullName = account.Moderators.FirstOrDefault()?.FullName ?? "Moderator";

            _logger.LogInformation("Login successful for {Email}. Identity: {FullName}, Role: {Role}", Email, fullName, account.Role);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, Email),
                new Claim("FullName", fullName),
                new Claim(ClaimTypes.Role, account.Role.ToString()),
                new Claim("AccountId", account.Id.ToString())
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

            if (account.Role == 2)
            {
                _logger.LogInformation("Redirecting User to Dashboard.");
                return RedirectToPage("/Dashboard");
            }
            else
            {
                _logger.LogInformation("Redirecting Admin/Mod to Index (Client pages restricted).");
                return RedirectToPage("/Index");
            }
        }

        _logger.LogWarning("Login failed for {Email}: Invalid credentials.", Email);
        ErrorMessage = "Invalid email or password.";
        return Page();
    }
}
