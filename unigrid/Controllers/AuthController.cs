using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using unigrid.Data;
using unigrid.Services;
using unigrid.Models;

namespace unigrid.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly UniGridDbContext _context;

        public AuthController(IAuthService authService, UniGridDbContext context)
        {
            _authService = authService;
            _context = context;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
            {
                return BadRequest("Invalid client request");
            }

            var account = await _authService.VerifyAccountAsync(request.Email, request.Password);
            if (account == null)
            {
                return Unauthorized(new { message = "Invalid email or password." });
            }

            var fullName = "System User";
            if (account.Role == 1) fullName = account.Admins.FirstOrDefault()?.FullName ?? "Admin";
            else if (account.Role == 2) fullName = account.Users.FirstOrDefault()?.FullName ?? "User";
            else if (account.Role == 3) fullName = account.Moderators.FirstOrDefault()?.FullName ?? "Moderator";

            var accessToken = _authService.GenerateAccessToken(account, fullName);
            var refreshToken = _authService.GenerateRefreshToken();

            account.RefreshToken = refreshToken;
            account.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                accessToken,
                refreshToken,
                fullName,
                email = account.Email,
                role = account.Role
            });
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] TokenApiModel request)
        {
            if (request == null || string.IsNullOrEmpty(request.AccessToken) || string.IsNullOrEmpty(request.RefreshToken))
            {
                return BadRequest("Invalid client request");
            }

            string accessToken = request.AccessToken;
            string refreshToken = request.RefreshToken;

            var principal = _authService.GetPrincipalFromExpiredToken(accessToken);
            if (principal == null)
            {
                return BadRequest("Invalid access token");
            }

            var email = principal.Identity?.Name;
            var account = await _context.Accounts
                .Include(a => a.Users)
                .Include(a => a.Admins)
                .Include(a => a.Moderators)
                .FirstOrDefaultAsync(a => a.Email == email);

            if (account == null || account.RefreshToken != refreshToken || account.RefreshTokenExpiry <= DateTime.UtcNow)
            {
                return BadRequest("Invalid client request");
            }

            var fullName = principal.FindFirst("FullName")?.Value ?? "System User";
            var newAccessToken = _authService.GenerateAccessToken(account, fullName);
            var newRefreshToken = _authService.GenerateRefreshToken();

            account.RefreshToken = newRefreshToken;
            account.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                accessToken = newAccessToken,
                refreshToken = newRefreshToken
            });
        }

        [HttpPost("revoke")]
        public async Task<IActionResult> Revoke()
        {
            var email = User.Identity?.Name;
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Email == email);
            if (account == null) return BadRequest();

            account.RefreshToken = null;
            account.RefreshTokenExpiry = null;
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpGet("google-simulated")]
        public IActionResult GoogleSimulated()
        {
            var html = @"<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Sign in - Google Accounts</title>
    <script src='https://cdn.tailwindcss.com'></script>
    <link href='https://fonts.googleapis.com/css2?family=Roboto:wght@400;500;700&display=swap' rel='stylesheet'>
    <style>
        body { font-family: 'Roboto', sans-serif; }
    </style>
</head>
<body class='bg-slate-50 flex items-center justify-center min-h-screen p-4'>
    <div class='bg-white border border-slate-200 rounded-lg p-10 max-w-[450px] w-full shadow-md text-center relative'>
        <!-- Google Logo -->
        <svg class='h-6 mx-auto mb-4' viewBox='0 0 24 24' width='74' height='24'>
            <path fill='#EA4335' d='M12 5.04c1.62 0 3.08.56 4.22 1.65l3.15-3.15C17.45 1.71 14.93 1 12 1 7.37 1 3.4 3.63 1.45 7.45l3.79 2.93C6.18 7.33 8.87 5.04 12 5.04z' />
            <path fill='#4285F4' d='M23.49 12.27c0-.81-.07-1.59-.2-2.36H12v4.47h6.44c-.28 1.47-1.11 2.71-2.36 3.55l3.67 2.84c2.15-1.98 3.39-4.9 3.39-8.5z' />
            <path fill='#FBBC05' d='M5.24 14.62c-.24-.73-.38-1.5-.38-2.31s.14-1.58.38-2.31L1.45 7.07C.52 8.94 0 11.02 0 13.23s.52 4.29 1.45 6.16l3.79-2.93z' />
            <path fill='#34A853' d='M12 23c3.24 0 5.96-1.08 7.95-2.93l-3.67-2.84c-1.02.68-2.33 1.09-3.8 1.09-3.13 0-5.82-2.29-6.77-5.34L.92 15.92C2.87 19.74 6.84 23 12 23z' />
        </svg>
        <h2 class='text-2xl font-normal text-slate-800 tracking-tight'>Choose an account</h2>
        <p class='text-slate-500 mt-1.5 text-sm'>to continue to <span class='font-medium text-slate-800'>UniGrid Platform</span></p>

        <div class='mt-6 space-y-2 text-left'>
            <!-- Account 1 -->
            <button onclick='selectGoogleAccount(""alice@student.edu"", ""Alice Nguyen"")' 
                    class='w-full p-3.5 border border-slate-200 hover:bg-slate-50 transition-all rounded-lg flex items-center gap-3'>
                <div class='h-9 w-9 bg-indigo-600 rounded-full flex items-center justify-center text-white text-xs font-bold'>AN</div>
                <div>
                    <div class='text-sm font-medium text-slate-700'>Alice Nguyen</div>
                    <div class='text-xs text-slate-500'>alice@student.edu</div>
                </div>
                <span class='ml-auto text-[10px] bg-slate-100 text-slate-600 px-2 py-0.5 rounded font-bold uppercase'>Existing</span>
            </button>

            <!-- Account 2 -->
            <button onclick='selectGoogleAccount(""bob@student.edu"", ""Bob Tran"")' 
                    class='w-full p-3.5 border border-slate-200 hover:bg-slate-50 transition-all rounded-lg flex items-center gap-3'>
                <div class='h-9 w-9 bg-emerald-600 rounded-full flex items-center justify-center text-white text-xs font-bold'>BT</div>
                <div>
                    <div class='text-sm font-medium text-slate-700'>Bob Tran</div>
                    <div class='text-xs text-slate-500'>bob@student.edu</div>
                </div>
                <span class='ml-auto text-[10px] bg-slate-100 text-slate-600 px-2 py-0.5 rounded font-bold uppercase'>Existing</span>
            </button>

            <!-- Account 3 -->
            <button onclick='selectGoogleAccount(""clara.google@student.edu"", ""Clara Le"")' 
                    class='w-full p-3.5 border border-slate-200 hover:bg-slate-50 transition-all rounded-lg flex items-center gap-3'>
                <div class='h-9 w-9 bg-indigo-100 text-indigo-700 rounded-full flex items-center justify-center text-xs font-bold'>CL</div>
                <div>
                    <div class='text-sm font-medium text-slate-700'>Clara Le</div>
                    <div class='text-xs text-slate-500'>clara.google@student.edu</div>
                </div>
                <span class='ml-auto text-[10px] bg-indigo-50 text-indigo-600 px-2 py-0.5 rounded font-bold uppercase'>New Guest</span>
            </button>
        </div>

        <div class='relative flex items-center justify-center my-6'>
            <div class='border-t border-slate-200 w-full'></div>
            <span class='absolute px-3 bg-white text-xs font-bold text-slate-400 uppercase tracking-wider'>Or use another email</span>
        </div>

        <form onsubmit='submitCustomGoogleEmail(event)' class='space-y-4 text-left'>
            <div class='space-y-1.5'>
                <label class='text-xs font-bold text-slate-400 uppercase tracking-widest'>Google Email Address</label>
                <input id='custom-email' type='email' required placeholder='username@gmail.com' 
                       class='w-full px-4 py-2.5 bg-slate-50 border border-slate-200 rounded-lg outline-none focus:ring-2 focus:ring-indigo-500/20 focus:border-indigo-500 transition-all font-medium text-sm text-slate-700'>
            </div>
            <button type='submit' class='w-full py-2.5 bg-[#1a73e8] hover:bg-blue-600 text-white rounded-lg text-sm font-bold transition-all'>
                Continue
            </button>
        </form>

        <p class='text-[11px] text-slate-400 mt-6 leading-relaxed'>
            To continue, Google will share your name, email address, language preference, and profile picture with UniGrid.
        </p>
    </div>

    <script>
        async function selectGoogleAccount(email, name) {
            try {
                const response = await fetch('/api/auth/google-signin-callback', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ email, name })
                });
                const result = await response.json();
                if (result.success) {
                    if (window.opener) {
                        window.opener.location.href = '/Dashboard';
                        window.close();
                    } else {
                        window.location.href = '/Dashboard';
                    }
                } else {
                    alert('Google sign-in failed: ' + result.message);
                }
            } catch (err) {
                console.error(err);
                alert('An error occurred during sign-in.');
            }
        }

        function submitCustomGoogleEmail(e) {
            e.preventDefault();
            const email = document.getElementById('custom-email').value;
            const name = email.split('@')[0].split('.').map(n => n.charAt(0).toUpperCase() + n.slice(1)).join(' ');
            selectGoogleAccount(email, name);
        }
    </script>
</body>
</html>";
            return new ContentResult
            {
                ContentType = "text/html",
                StatusCode = 200,
                Content = html
            };
        }

        [HttpPost("google-signin-callback")]
        public async Task<IActionResult> GoogleSignInCallback([FromBody] GoogleSignInRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest(new { success = false, message = "Invalid email payload." });
            }

            var email = request.Email.Trim().ToLower();
            var account = await _context.Accounts
                .Include(a => a.Users)
                .Include(a => a.Admins)
                .Include(a => a.Moderators)
                .FirstOrDefaultAsync(a => a.Email.ToLower() == email);

            string fullName = request.Name ?? "New Google User";

            if (account == null)
            {
                // Register a new Account via Google sign-in
                account = new Account
                {
                    Id = Guid.NewGuid(),
                    Email = request.Email.Trim(),
                    PasswordHash = "GOOGLE_OAUTH", // Flag representing Google Social OAuth
                    Role = 2, // User Role
                    IsLocked = false,
                    CreatedAt = DateTime.UtcNow
                };
                await _context.Accounts.AddAsync(account);
                await _context.SaveChangesAsync();

                // Load relationships (Users will be empty)
                account = await _context.Accounts
                    .Include(a => a.Users)
                    .FirstOrDefaultAsync(a => a.Id == account.Id);

                fullName = "New Google User";
            }
            else
            {
                if (account.Role == 1) fullName = account.Admins.FirstOrDefault()?.FullName ?? "Admin";
                else if (account.Role == 2) fullName = account.Users.FirstOrDefault()?.FullName ?? "New Google User";
                else if (account.Role == 3) fullName = account.Moderators.FirstOrDefault()?.FullName ?? "Moderator";
            }

            // Create Authentication Cookie Claims
            var claims = new System.Collections.Generic.List<Claim>
            {
                new Claim(ClaimTypes.Name, account.Email),
                new Claim("FullName", fullName),
                new Claim(ClaimTypes.Role, account.Role.ToString()),
                new Claim("AccountId", account.Id.ToString())
            };

            var claimsIdentity = new ClaimsIdentity(claims, Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);

            var authProperties = new Microsoft.AspNetCore.Authentication.AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            };

            await HttpContext.SignInAsync(
                Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            // Generate JWT access/refresh tokens to prevent conflict with JWT clients
            var accessToken = _authService.GenerateAccessToken(account, fullName);
            var refreshToken = _authService.GenerateRefreshToken();

            account.RefreshToken = refreshToken;
            account.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
            await _context.SaveChangesAsync();

            return Ok(new 
            { 
                success = true,
                accessToken,
                refreshToken,
                fullName,
                email = account.Email,
                role = account.Role
            });
        }

        [HttpGet("google-login")]
        public IActionResult GoogleLogin()
        {
            var redirectUrl = Url.Action(nameof(GoogleCallback), "Auth");
            var properties = new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = redirectUrl };
            return Challenge(properties, Microsoft.AspNetCore.Authentication.Google.GoogleDefaults.AuthenticationScheme);
        }

        [HttpGet("google-callback")]
        public async Task<IActionResult> GoogleCallback()
        {
            var result = await HttpContext.AuthenticateAsync("ExternalCookies");
            if (!result.Succeeded || result.Principal == null)
            {
                return Redirect("/Login?error=Google+authentication+failed");
            }

            var externalUser = result.Principal;
            var email = externalUser.FindFirstValue(ClaimTypes.Email) ?? externalUser.FindFirstValue(ClaimTypes.Name);
            var name = externalUser.FindFirstValue(ClaimTypes.Name) ?? "New Google User";

            if (string.IsNullOrEmpty(email))
            {
                return Redirect("/Login?error=Email+claim+not+found+from+Google");
            }

            email = email.Trim().ToLower();
            var account = await _context.Accounts
                .Include(a => a.Users)
                .Include(a => a.Admins)
                .Include(a => a.Moderators)
                .FirstOrDefaultAsync(a => a.Email.ToLower() == email);

            string fullName = name;

            if (account == null)
            {
                // Register a new Account via Google sign-in
                account = new Account
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    PasswordHash = "GOOGLE_OAUTH", // Flag representing Google Social OAuth
                    Role = 2, // User Role
                    IsLocked = false,
                    CreatedAt = DateTime.UtcNow
                };
                await _context.Accounts.AddAsync(account);
                await _context.SaveChangesAsync();

                // Load relationships (Users will be empty)
                account = await _context.Accounts
                    .Include(a => a.Users)
                    .FirstOrDefaultAsync(a => a.Id == account.Id);

                fullName = "New Google User";
            }
            else
            {
                if (account.Role == 1) fullName = account.Admins.FirstOrDefault()?.FullName ?? "Admin";
                else if (account.Role == 2) fullName = account.Users.FirstOrDefault()?.FullName ?? "New Google User";
                else if (account.Role == 3) fullName = account.Moderators.FirstOrDefault()?.FullName ?? "Moderator";
            }

            // Create Authentication Cookie Claims
            var claims = new System.Collections.Generic.List<Claim>
            {
                new Claim(ClaimTypes.Name, account.Email),
                new Claim("FullName", fullName),
                new Claim(ClaimTypes.Role, account.Role.ToString()),
                new Claim("AccountId", account.Id.ToString())
            };

            var claimsIdentity = new ClaimsIdentity(claims, Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);

            var authProperties = new Microsoft.AspNetCore.Authentication.AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            };

            await HttpContext.SignInAsync(
                Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            // Generate JWT access/refresh tokens to prevent conflict with JWT clients
            var accessToken = _authService.GenerateAccessToken(account, fullName);
            var refreshToken = _authService.GenerateRefreshToken();

            account.RefreshToken = refreshToken;
            account.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
            await _context.SaveChangesAsync();

            // Clear external cookie
            await HttpContext.SignOutAsync("ExternalCookies");

            return Redirect("/Dashboard");
        }
    }

    public class GoogleSignInRequest
    {
        public string Email { get; set; } = null!;
        public string? Name { get; set; }
    }

    public class LoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class TokenApiModel
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
    }
}
