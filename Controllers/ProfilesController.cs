using BookNerd.Domain.Services;
using BookNerd.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Folio.Api.Controllers;

[ApiController, Route("api/profiles")]
public class ProfilesController(UserManager<AppUser> users, IFileStorage storage) : ControllerBase
{
    [HttpGet("{id}/avatar")]
    public async Task<IActionResult> Avatar(string id, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id);
        if (user?.AvatarKey == null) return NotFound();
        var stream = await storage.OpenAsync(user.AvatarKey, ct);
        return stream == null ? NotFound() : File(stream, BooksController.ImageType(user.AvatarKey));
    }
    [Authorize, HttpPost("avatar"), RequestSizeLimit(2_100_000)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        var extension = await BooksController.ValidateImage(file, ct);
        if (extension == null) return BadRequest(new { message = "Choose a PNG, JPEG, or WebP up to 2 MB." });
        var user = (await users.GetUserAsync(User))!; var old = user.AvatarKey;
        await using var stream = file.OpenReadStream(); user.AvatarKey = await storage.SaveAsync(stream, extension, BooksController.ImageType(extension), ct);
        await users.UpdateAsync(user);
        if (old != null) await storage.DeleteAsync(old, ct);
        return Ok(new { avatarUrl = $"/api/profiles/{user.Id}/avatar" });
    }
}
