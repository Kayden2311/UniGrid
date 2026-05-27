using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
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
