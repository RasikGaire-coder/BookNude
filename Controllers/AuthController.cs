using System.Security.Claims;
using BookNerd.Infrastructure.Identity;
using Folio.Api.Data;
using Folio.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Folio.Api.Controllers;

[ApiController, Route("api/auth"), EnableRateLimiting("auth")]
public class AuthController(UserManager<AppUser> users, TokenService tokens, FolioDbContext db, EmailSender email, IConfiguration config, IWebHostEnvironment environment) : ControllerBase
{
    private const string RefreshCookie = "booknerd_refresh";
    private void SetRefresh(string value) => Response.Cookies.Append(RefreshCookie, value, new CookieOptions {
        HttpOnly = true, Secure = !environment.IsDevelopment() || Request.IsHttps,
        SameSite = SameSiteMode.Strict, Path = "/api/auth", MaxAge = TimeSpan.FromDays(14), IsEssential = true
    });
    private void ClearRefresh() => Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/api/auth", SameSite = SameSiteMode.Strict, Secure = !environment.IsDevelopment() || Request.IsHttps });
    private async Task<object> UserDto(AppUser user) => new { user.Id, user.Email, user.DisplayName, user.ReadingGoal, user.SpoilerShield, avatarUrl = user.AvatarKey == null ? null : $"/api/profiles/{user.Id}/avatar", roles = await users.GetRolesAsync(user) };

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto input)
    {
        var user = new AppUser { UserName = input.Email.Trim(), Email = input.Email.Trim(), DisplayName = input.DisplayName.Trim() };
        var result = await users.CreateAsync(user, input.Password);
        if (!result.Succeeded) return BadRequest(new { message = "Could not create account.", errors = result.Errors.Select(x => x.Description) });
        await users.AddToRoleAsync(user, "User");
        var issued = await tokens.IssueAsync(user);
        SetRefresh(issued.Refresh);
        return Ok(new { accessToken = issued.Access, user = await UserDto(user), expiresIn = 900 });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto input)
    {
        var user = await users.FindByEmailAsync(input.Email.Trim());
        if (user == null || await users.IsLockedOutAsync(user)) return Unauthorized(new { message = "Email or password is incorrect, or the account is temporarily locked." });
        if (!await users.CheckPasswordAsync(user, input.Password))
        {
            await users.AccessFailedAsync(user);
            return Unauthorized(new { message = "Email or password is incorrect, or the account is temporarily locked." });
        }
        await users.ResetAccessFailedCountAsync(user);
        var issued = await tokens.IssueAsync(user);
        SetRefresh(issued.Refresh);
        return Ok(new { accessToken = issued.Access, user = await UserDto(user), expiresIn = 900 });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        if (Request.Headers["X-BookNerd-Client"] != "web") return BadRequest(new { message = "Missing client header." });
        var cookie = Request.Cookies[RefreshCookie];
        if (string.IsNullOrEmpty(cookie)) return Unauthorized();
        var issued = await tokens.RotateAsync(cookie);
        if (issued == null) { ClearRefresh(); return Unauthorized(); }
        SetRefresh(issued.Value.Refresh);
        return Ok(new { accessToken = issued.Value.Access, user = await UserDto(issued.Value.User), expiresIn = 900 });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (Request.Headers["X-BookNerd-Client"] != "web") return BadRequest();
        var cookie = Request.Cookies[RefreshCookie];
        if (cookie != null)
        {
            var hash = TokenService.Hash(cookie);
            var session = await db.RefreshSessions.SingleOrDefaultAsync(x => x.TokenHash == hash);
            if (session != null) await db.RefreshSessions.Where(x => x.FamilyId == session.FamilyId).ExecuteUpdateAsync(set => set.SetProperty(x => x.RevokedAt, DateTime.UtcNow));
        }
        ClearRefresh();
        return NoContent();
    }

    [Authorize, HttpGet("me")]
    public async Task<IActionResult> Me() => Ok(await UserDto((await users.GetUserAsync(User))!));

    [Authorize, HttpPut("profile")]
    public async Task<IActionResult> Profile(ProfileDto input)
    {
        var user = (await users.GetUserAsync(User))!;
        user.DisplayName = input.DisplayName.Trim(); user.ReadingGoal = input.ReadingGoal; user.SpoilerShield = input.SpoilerShield;
        var result = await users.UpdateAsync(user);
        return result.Succeeded ? Ok(await UserDto(user)) : BadRequest(new { message = "Could not update profile." });
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto input)
    {
        var user = await users.FindByEmailAsync(input.Email.Trim());
        if (user != null)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var origin = (config["App:FrontendUrl"] ?? "https://localhost:7201").TrimEnd('/');
            await email.SendResetAsync(user.Email!, $"{origin}/reset-password?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}");
        }
        return Ok(new { message = "If that account exists, a password reset link has been sent." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordDto input)
    {
        var user = await users.FindByEmailAsync(input.Email);
        if (user == null) return BadRequest(new { message = "Reset link is invalid or expired." });
        var result = await users.ResetPasswordAsync(user, input.Token, input.Password);
        if (!result.Succeeded) return BadRequest(new { message = "Reset link is invalid, expired, or the password does not meet requirements.", errors = result.Errors.Select(x => x.Description) });
        await tokens.RevokeUserAsync(user.Id);
        ClearRefresh();
        return Ok(new { message = "Password updated. Please sign in again." });
    }
}
