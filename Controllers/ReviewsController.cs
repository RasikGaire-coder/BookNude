using System.Security.Claims;
using BookNerd.Domain.Services;
using BookNerd.Infrastructure.Identity;
using Folio.Api.Data;
using Folio.Api.DTOs;
using Folio.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Folio.Api.Controllers;

[ApiController, Route("api")]
public class ReviewsController(FolioDbContext db, UserManager<AppUser> users) : ControllerBase
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet("books/{bookId:int}/reviews")]
    public async Task<IActionResult> List(int bookId, [FromQuery] int page = 1, [FromQuery] bool reveal = false)
    {
        if (!await db.Books.AnyAsync(b => b.BookId == bookId && b.IsActive)) return NotFound();
        var user = UserId == null ? null : await users.GetUserAsync(User);
        var chapter = UserId == null ? 0 : await db.ReadingProgress.Where(p => p.BookId == bookId && p.UserId == UserId).Select(p => p.FurthestChapter).SingleOrDefaultAsync();
        var query = db.Reviews.AsNoTracking().Where(x => x.BookId == bookId && x.ModerationStatus == "Visible");
        var total = await query.CountAsync();
        var rows = await query.OrderByDescending(x => x.CreatedAt).Skip((Math.Max(1, page) - 1) * 10).Take(10).Select(x => new {
            x.Id, x.UserId, x.Stars, x.Comment, x.HasSpoilers, x.SpoilerChapter, x.CreatedAt,
            author = db.Users.Where(u => u.Id == x.UserId).Select(u => u.DisplayName).First(),
            score = x.Votes.Sum(v => (int?)v.Value) ?? 0,
            myVote = x.Votes.Where(v => v.UserId == UserId).Select(v => v.Value).FirstOrDefault()
        }).ToListAsync();
        return Ok(new { items = rows.Select(x => { var hidden = !reveal && RecommendationMath.HideSpoiler(user?.SpoilerShield ?? true, x.HasSpoilers, x.SpoilerChapter, chapter); return new { x.Id, rating = x.Stars, comment = hidden ? null : x.Comment, x.HasSpoilers, x.SpoilerChapter, x.CreatedAt, x.author, x.score, x.myVote, hidden, isOwn = x.UserId == UserId }; }), totalItems = total, totalPages = (int)Math.Ceiling(total / 10d), page = Math.Max(1, page) });
    }

    [Authorize, EnableRateLimiting("write"), HttpPost("books/{bookId:int}/reviews")]
    public async Task<IActionResult> Save(int bookId, ReviewDto input)
    {
        var book = await db.Books.SingleOrDefaultAsync(x => x.BookId == bookId && x.IsActive);
        if (book == null) return NotFound();
        if (input.SpoilerChapter > book.ChapterCount && book.ChapterCount > 0) return BadRequest(new { message = "Spoiler chapter exceeds the book length." });
        var review = await db.Reviews.SingleOrDefaultAsync(x => x.BookId == bookId && x.UserId == UserId);
        if (review == null) { review = new Review { BookId = bookId, UserId = UserId! }; db.Reviews.Add(review); }
        review.Stars = input.Rating; review.Comment = input.Comment.Trim();
        // Any supplied chapter is automatically treated as spoiler content.
        review.HasSpoilers = input.HasSpoilers || input.SpoilerChapter.HasValue;
        review.SpoilerChapter = input.SpoilerChapter; review.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(); return Ok(new { review.Id });
    }

    [Authorize, HttpDelete("reviews/{id:int}")]
    public async Task<IActionResult> Delete(int id) { var removed = await db.Reviews.Where(x => x.Id == id && x.UserId == UserId).ExecuteDeleteAsync(); return removed == 0 ? NotFound() : NoContent(); }

    [Authorize, EnableRateLimiting("write"), HttpPut("reviews/{id:int}/vote")]
    public async Task<IActionResult> Vote(int id, VoteDto input)
    {
        var review = await db.Reviews.SingleOrDefaultAsync(x => x.Id == id && x.ModerationStatus == "Visible");
        if (review == null) return NotFound();
        if (review.UserId == UserId) return BadRequest(new { message = "You cannot vote on your own review." });
        var vote = await db.ReviewVotes.FindAsync(id, UserId);
        if (input.Value == 0) { if (vote != null) db.ReviewVotes.Remove(vote); }
        else if (vote == null) db.ReviewVotes.Add(new ReviewVote { ReviewId = id, UserId = UserId!, Value = input.Value });
        else vote.Value = input.Value;
        await db.SaveChangesAsync(); return NoContent();
    }

    [Authorize, EnableRateLimiting("write"), HttpPost("reviews/{id:int}/flag")]
    public async Task<IActionResult> Flag(int id, FlagDto input)
    {
        if (!await db.Reviews.AnyAsync(x => x.Id == id && x.ModerationStatus == "Visible")) return NotFound();
        if (!await db.ReviewFlags.AnyAsync(x => x.ReviewId == id && x.UserId == UserId)) { db.ReviewFlags.Add(new ReviewFlag { ReviewId = id, UserId = UserId!, Reason = input.Reason.Trim() }); await db.SaveChangesAsync(); }
        return Ok(new { message = "Thanks. An administrator will review your report." });
    }

    [Authorize(Roles = "Admin"), HttpGet("admin/reviews")]
    public async Task<IActionResult> Queue([FromQuery] int page = 1) => Ok(await db.Reviews.AsNoTracking().Where(x => x.Flags.Any(f => !f.Resolved)).OrderBy(x => x.CreatedAt).Skip((Math.Max(page, 1) - 1) * 20).Take(20).Select(x => new { x.Id, bookTitle = x.Book.Title, x.Comment, rating = x.Stars, x.ModerationStatus, flags = x.Flags.Where(f => !f.Resolved).Select(f => new { f.Id, f.Reason, f.CreatedAt }) }).ToListAsync());

    [Authorize(Roles = "Admin"), HttpPut("admin/reviews/{id:int}")]
    public async Task<IActionResult> Moderate(int id, ModerateDto input)
    {
        var review = await db.Reviews.Include(x => x.Flags).SingleOrDefaultAsync(x => x.Id == id);
        if (review == null) return NotFound();
        review.ModerationStatus = input.Status;
        foreach (var flag in review.Flags) flag.Resolved = true;
        await db.SaveChangesAsync(); return NoContent();
    }
}
