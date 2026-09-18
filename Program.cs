using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using BookNerd.Domain.Services;
using BookNerd.Infrastructure.Ai;
using BookNerd.Infrastructure.Data;
using BookNerd.Infrastructure.Identity;
using BookNerd.Infrastructure.Storage;
using Folio.Api.Data;
using Folio.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.BookNerd.json", optional: true).AddEnvironmentVariables().AddCommandLine(args);
var config = builder.Configuration;

if (builder.Environment.IsDevelopment() && string.IsNullOrEmpty(config["Jwt:Key"]))
{
    var directory = Path.Combine(builder.Environment.ContentRootPath, "App_Data"); Directory.CreateDirectory(directory);
    var keyPath = Path.Combine(directory, "jwt.key");
    if (!File.Exists(keyPath)) File.WriteAllText(keyPath, Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));
    config["Jwt:Key"] = File.ReadAllText(keyPath);
}
if (Encoding.UTF8.GetByteCount(config["Jwt:Key"] ?? "") < 32) throw new InvalidOperationException("Set Jwt__Key to a random secret of at least 32 bytes.");

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => {
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "BookNerd API", Version = "v1", Description = "Catalog, accounts, reading, reviews, and discovery tools." });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>() });
});
builder.Services.AddDbContext<FolioDbContext>(options => options.UseSqlServer(config.GetConnectionString("DefaultConnection"), sql => sql.MigrationsAssembly("BookNerd.Infrastructure")));
builder.Services.AddIdentityCore<AppUser>(options => {
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 12; options.Password.RequireDigit = true; options.Password.RequireLowercase = true; options.Password.RequireUppercase = true; options.Password.RequireNonAlphanumeric = true;
    options.Lockout.MaxFailedAccessAttempts = 5; options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddRoles<IdentityRole>().AddEntityFrameworkStores<FolioDbContext>().AddDefaultTokenProviders();
builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(1));
var keysPath = config["DataProtection:Path"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
Directory.CreateDirectory(keysPath);
builder.Services.AddDataProtection().SetApplicationName("BookNerd").PersistKeysToFileSystem(new DirectoryInfo(keysPath));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => {
    options.TokenValidationParameters = new TokenValidationParameters {
        ValidateIssuer = true, ValidIssuer = config["Jwt:Issuer"], ValidateAudience = true, ValidAudience = config["Jwt:Audience"],
        ValidateLifetime = true, ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), ClockSkew = TimeSpan.FromSeconds(20)
    };
    options.Events = new JwtBearerEvents { OnTokenValidated = async context => {
        var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
        var user = await users.GetUserAsync(context.Principal!);
        if (user == null || user.SecurityStamp != context.Principal!.FindFirstValue("stamp") || await users.IsLockedOutAsync(user)) context.Fail("Session is no longer valid.");
    } };
});
builder.Services.AddAuthorization();
builder.Services.AddCors(options => options.AddPolicy("ReactCors", policy => policy.WithOrigins(config.GetSection("App:CorsOrigins").Get<string[]>() ?? ["http://localhost:5173"]).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.Configure<ForwardedHeadersOptions>(options => {
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    if (IPAddress.TryParse(config["ReverseProxy:KnownProxy"], out var address)) options.KnownProxies.Add(address);
});
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = 429;
    options.OnRejected = async (context, ct) => { context.HttpContext.Response.Headers.RetryAfter = "60"; await context.HttpContext.Response.WriteAsJsonAsync(new { message = "Too many requests. Please try again in a minute." }, ct); };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 240, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    foreach (var rule in new[] { ("auth", 30), ("ai", 10), ("write", 30) }) options.AddPolicy(rule.Item1, context => RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = rule.Item2, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddScoped<TokenService>(); builder.Services.AddScoped<EmailSender>();
builder.Services.AddSingleton<LocalFileStorage>();
builder.Services.AddSingleton<IFileStorage>(services => config["Storage:Provider"] == "Azure" ? new AzureFileStorage(config, services.GetRequiredService<LocalFileStorage>()) : services.GetRequiredService<LocalFileStorage>());
builder.Services.AddHttpClient<OpenAiService>(client => client.Timeout = TimeSpan.FromSeconds(45));

var app = builder.Build();
app.UseForwardedHeaders();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment()) app.UseHsts();
if (!config.GetValue("App:DisableHttpsRedirection", false)) app.UseHttpsRedirection();
app.Use(async (context, next) => {
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    if (context.Request.Path.StartsWithSegments("/api/me") || context.Request.Path.StartsWithSegments("/api/auth")) context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.UseRouting(); app.UseCors("ReactCors"); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
app.UseSwagger(); app.UseSwaggerUI(options => options.DocumentTitle = "BookNerd API");
app.MapControllers();
app.MapGet("/health", async (FolioDbContext db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "healthy" }) : Results.StatusCode(503));
app.MapGet("/api/status", async (OpenAiService ai, FolioDbContext db) => Results.Ok(new { name = "BookNerd", aiConfigured = ai.Configured, indexedBooks = await db.BookEmbeddings.CountAsync(), storage = config["Storage:Provider"] ?? "Local" }));
app.MapFallback("/api/{**path}", () => Results.NotFound(new { message = "API endpoint not found." }));
app.MapFallbackToFile("index.html");

if (config.GetValue("Database:MigrateOnStartup", false) || args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<FolioDbContext>().Database.MigrateAsync();
}
if (config.GetValue("Database:SeedOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await DatabaseInitializer.InitializeAsync(scope.ServiceProvider.GetRequiredService<FolioDbContext>(), scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>(), scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(), config);
}
if (!args.Contains("--migrate-only")) app.Run();
public partial class Program { }
