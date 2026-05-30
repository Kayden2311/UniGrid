using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddSignalR();
builder.Services.AddMemoryCache();

// Register Services
builder.Services.AddScoped<unigrid.Services.IAuthService, unigrid.Services.AuthService>();

// Enable Data Protection Key Persistence to keep session states active across app executions/restarts
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, ".keys")));

// Configure Antiforgery Cookie Policy for Lax security in local HTTP environments
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "UniGrid.Antiforgery";
    options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
    options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
});

// Register DB Context with automatic retry policies for SQL Server cold-starts
builder.Services.AddDbContext<unigrid.Data.UniGridDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlServerOptionsAction: sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
        }));

// JWT Authentication
var jwtSettings = builder.Configuration.GetSection("Jwt");
var key = System.Text.Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? "super_secret_unigrid_key_2024_placeholder_must_be_long");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.Name = "UniGrid.Auth";
    options.LoginPath = "/Login";
    options.LogoutPath = "/Logout";
    options.AccessDeniedPath = "/Error";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key)
    };
});

// Configure default authorization policy to support both Cookie and JWT Bearer schemes
builder.Services.AddAuthorization(options =>
{
    var defaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(
        Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
        Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme);
    defaultPolicy = defaultPolicy.RequireAuthenticatedUser();
    options.DefaultPolicy = defaultPolicy.Build();
});

// Configure Forwarded Headers to support port forwarding, HTTPS proxies, and remote IDE webviews
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | 
                               Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto | 
                               Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

// Custom Request Logging Middleware to diagnose Login/Signup blocks
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    if (context.Request.Path.Value?.Contains("Login", StringComparison.OrdinalIgnoreCase) == true || 
        context.Request.Path.Value?.Contains("Signup", StringComparison.OrdinalIgnoreCase) == true)
    {
        logger.LogInformation(">>> [Login/Signup Request] {Method} {Path}{QueryString}", 
            context.Request.Method, context.Request.Path, context.Request.QueryString);
        foreach (var header in context.Request.Headers)
        {
            logger.LogInformation(">>> Header: {Key} = {Value}", header.Key, header.Value);
        }
    }
    
    await next();
    
    if (context.Request.Path.Value?.Contains("Login", StringComparison.OrdinalIgnoreCase) == true || 
        context.Request.Path.Value?.Contains("Signup", StringComparison.OrdinalIgnoreCase) == true)
    {
        logger.LogInformation(">>> [Login/Signup Response] {StatusCode} for {Method} {Path}", 
            context.Response.StatusCode, context.Request.Method, context.Request.Path);
    }
});


// Auto-create and seed database at startup with a robust retry loop for SQL Server cold-starts
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var context = services.GetRequiredService<unigrid.Data.UniGridDbContext>();
    
    int retryCount = 0;
    int maxRetries = 6;
    bool seedSuccess = false;
    while (retryCount < maxRetries && !seedSuccess)
    {
        try
        {
            // Execute centralized initializer
            await unigrid.Data.DbInitializer.InitializeAndSeedAsync(context, logger);
            seedSuccess = true;
        }
        catch (Exception ex)
        {
            retryCount++;
            logger.LogWarning(ex, "Database initialization/seeding failed on attempt {Attempt}/{MaxRetries}. Retrying in 5 seconds...", retryCount, maxRetries);
            if (retryCount >= maxRetries)
            {
                logger.LogError(ex, "Database seeding failed after maximum retries. The application will proceed but may encounter missing tables.");
            }
            else
            {
                System.Threading.Thread.Sleep(5000);
            }
        }
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    if (path != null && path.StartsWith("/files/", StringComparison.OrdinalIgnoreCase))
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            var workspaceIdStr = parts[1];
            if (Guid.TryParse(workspaceIdStr, out var workspaceId))
            {
                // Authenticate manually since UseAuthentication hasn't run yet in the pipeline for static files
                var authResult = await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.AuthenticateAsync(
                    context, 
                    Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);

                if (!authResult.Succeeded || authResult.Principal?.Identity?.IsAuthenticated != true)
                {
                    context.Response.StatusCode = 401; // Unauthorized
                    return;
                }

                var userPrincipal = authResult.Principal;
                var accountIdClaim = userPrincipal.FindFirst("AccountId")?.Value;
                if (string.IsNullOrEmpty(accountIdClaim))
                {
                    context.Response.StatusCode = 401;
                    return;
                }

                var accountId = Guid.Parse(accountIdClaim);
                using (var scope = context.RequestServices.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<unigrid.Data.UniGridDbContext>();
                    var dbUser = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                        dbContext.Users, 
                        u => u.AccountId == accountId);

                    if (dbUser == null)
                    {
                        context.Response.StatusCode = 403; // Forbidden
                        return;
                    }

                    // Check workspace membership or ownership
                    var isOwner = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(
                        dbContext.Workspaces, 
                        w => w.Id == workspaceId && w.OwnerId == dbUser.Id);

                    var isMember = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(
                        dbContext.WorkspaceMembers, 
                        wm => wm.WorkspaceId == workspaceId && wm.UserId == dbUser.Id);

                    if (!isOwner && !isMember)
                    {
                        context.Response.StatusCode = 403; // Forbidden
                        return;
                    }
                }
            }
        }
    }
    await next();
});
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
app.MapControllers();
app.MapHub<unigrid.Hubs.ChatHub>("/chatHub");

app.Run();
