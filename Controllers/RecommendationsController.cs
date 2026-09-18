using System.Security.Claims;
using BookNerd.Domain.Services;
using Folio.Api.Data;
using Folio.Api.Models;
using Folio.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Folio.Api.Controllers;

[ApiController, Route("api/recommendations")]
public class RecommendationsController(FolioDbContext db) : ControllerBase
{
    [HttpGet("tags")]
    public IActionResult Tags() => Ok(NarrativeTags.All);

    [HttpGet("structure")]
    public async Task<IActionResult> Structure([FromQuery] string tags = "", [FromQuery] int page = 1)
    {
        var selected = tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
        if (selected.Any(t => !NarrativeTags.All.Contains(t))) return BadRequest(new { message = "Unknown story structure tag." });
        var query = db.Books.Where(b => b.IsActive);
        foreach (var tag in selected) { var term = tag; query = query.Where(b => b.StoryTags.Any(t => t.Name == term)); }
        var total = await query.CountAsync();
        return Ok(new { items = await CatalogProjection.Books(db, query).OrderByDescending(b => b.AverageRating).ThenBy(b => b.BookId).Skip((Math.Max(1, page) - 1) * 12).Take(12).ToListAsync(), totalItems = total, totalPages = (int)Math.Ceiling(total / 12d), page = Math.Max(1, page) });
    }

    [HttpGet("trending")]
    public async Task<IActionResult> Trending([FromQuery] string period = "week", [FromQuery] int? genre = null)
    {
        if (period is not ("week" or "month")) return BadRequest(new { message = "Use week or month." });
        var since = DateTime.UtcNow.Date.AddDays(period == "week" ? -7 : -30);
        var candidates = db.Books.Where(b => b.IsActive && (!genre.HasValue || b.GenreId == genre));
        var metrics = await candidates.Select(b => new {
            b.BookId,
            readers = db.ReadingEvents.Where(e => e.BookId == b.BookId && e.Day >= since).Select(e => e.UserId).Distinct().Count(),
            pages = db.ReadingEvents.Where(e => e.BookId == b.BookId && e.Day >= since).Sum(e => (int?)e.PagesRead) ?? 0,
            reviews = db.Reviews.Count(r => r.BookId == b.BookId && r.CreatedAt >= since && r.ModerationStatus == "Visible"),
            rating = db.Reviews.Where(r => r.BookId == b.BookId && r.ModerationStatus == "Visible").Average(r => (double?)r.Stars) ?? (b.Rating == null ? 0 : (double)b.Rating.AverageScore)
        }).ToListAsync();
        var ranked = metrics.Select(m => new { m.BookId, m.readers, m.pages, m.reviews, score = RecommendationMath.Trending(m.readers, m.pages, m.reviews, m.rating) }).OrderByDescending(m => m.score).ThenBy(m => m.BookId).Take(12).ToList();
        var ids = ranked.Select(m => m.BookId).ToArray();
        var books = await CatalogProjection.Books(db, db.Books.Where(b => ids.Contains(b.BookId))).ToDictionaryAsync(b => b.BookId);
        return Ok(new { period, since, formula = "3 × unique readers + log2(1 + new pages) + 2 × new reviews + rating", items = ranked.Select((m, index) => new { rank = index + 1, book = books[m.BookId], m.readers, m.pages, m.reviews, score = Math.Round(m.score, 2) }) });
    }

    [Authorize, HttpGet("for-you")]
    public async Task<IActionResult> ForYou()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var likedIds = await db.Reviews.Where(r => r.UserId == id && r.Stars >= 4).Select(r => r.BookId).ToListAsync();
        var readIds = await db.ReadingProgress.Where(p => p.UserId == id && p.Status != "want").Select(p => p.BookId).ToListAsync();
        var sourceIds = likedIds.Concat(readIds).Distinct().ToArray();
        var favoriteGenres = await db.Books.Where(b => sourceIds.Contains(b.BookId)).Select(b => b.GenreId).Distinct().ToListAsync();
        var favoriteTags = await db.StoryTags.Where(t => sourceIds.Contains(t.BookId)).Select(t => t.Name).Distinct().ToListAsync();
        var ids = await db.Books.Where(b => b.IsActive && !readIds.Contains(b.BookId))
            .OrderByDescending(b => (favoriteGenres.Contains(b.GenreId) ? 2 : 0) + b.StoryTags.Count(t => favoriteTags.Contains(t.Name)))
            .ThenByDescending(b => b.Rating == null ? 0 : b.Rating.AverageScore).Take(12).Select(b => b.BookId).ToListAsync();
        var books = await CatalogProjection.Books(db, db.Books.Where(b => ids.Contains(b.BookId))).ToDictionaryAsync(b => b.BookId);
        return Ok(new { basis = sourceIds.Length > 0 ? "Based on your reading history, highly rated books, genres, and story structures." : "Start reading or rating books to personalize this shelf.", items = ids.Select(bookId => books[bookId]) });
    }
}
