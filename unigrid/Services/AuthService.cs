using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using unigrid.Data;
using unigrid.Models;
using Microsoft.Extensions.Logging;

namespace unigrid.Services;

public interface IAuthService
{
    Task<Account?> VerifyAccountAsync(string email, string password);
    string GenerateToken(Account account, string fullName);
    string GenerateAccessToken(Account account, string fullName);
    string GenerateRefreshToken();
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}

public class AuthService : IAuthService
{
    private readonly IConfiguration _configuration;
    private readonly UniGridDbContext _context;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IConfiguration configuration, UniGridDbContext context, ILogger<AuthService> logger)
    {
        _configuration = configuration;
        _context = context;
        _logger = logger;
    }

    public async Task<Account?> VerifyAccountAsync(string email, string password)
    {
        _logger.LogInformation("Attempting to verify account for email: {Email}", email);

        var account = await _context.Accounts
            .Include(a => a.Users)
            .Include(a => a.Admins)
            .Include(a => a.Moderators)
            .FirstOrDefaultAsync(a => a.Email == email);

        if (account == null)
        {
            _logger.LogWarning("Account not found for email: {Email}", email);
            return null;
        }

        _logger.LogInformation("Account found. Verifying password for role: {Role}", account.Role);

        // Simple check for demo/seed data
        if (password == "password123" || password == account.PasswordHash)
        {
            _logger.LogInformation("Password verification successful for {Email}", email);
            return account;
        }

        _logger.LogWarning("Password verification failed for {Email}", email);
        return null;
    }

    public string GenerateToken(Account account, string fullName)
    {
        return GenerateAccessToken(account, fullName);
    }

    public string GenerateAccessToken(Account account, string fullName)
    {
        _logger.LogInformation("Generating Access Token for {Email} ({FullName})", account.Email, fullName);
        
        var jwtSettings = _configuration.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? "super_secret_unigrid_key_2024_placeholder_must_be_long"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, account.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("AccountId", account.Id.ToString()),
            new Claim("FullName", fullName),
            new Claim(ClaimTypes.Role, account.Role.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"],
            audience: jwtSettings["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15), // 15-minute short-lived access token
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        _logger.LogInformation("Generating cryptographically secure Refresh Token");
        var randomNumber = new byte[64];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }
    }

    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        _logger.LogInformation("Parsing claims from expired Access Token");
        var jwtSettings = _configuration.GetSection("Jwt");
        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? "super_secret_unigrid_key_2024_placeholder_must_be_long")),
            ValidateLifetime = false // ignore lifetime check to read expired token claims
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        SecurityToken securityToken;
        var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out securityToken);
        var jwtSecurityToken = securityToken as JwtSecurityToken;
        if (jwtSecurityToken == null || !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            throw new SecurityTokenException("Invalid token");

        return principal;
    }
}
