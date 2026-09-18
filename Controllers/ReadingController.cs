using System.Security.Claims;
using BookNerd.Domain.Services;
using BookNerd.Infrastructure.Identity;
using Folio.Api.Data;
using Folio.Api.DTOs;
using Folio.Api.Models;
using Folio.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Folio.Api.Controllers;

[ApiController, Authorize, Route("api/me")]
public class ReadingController(FolioDbContext db, UserManager<AppUser> users) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet("shelf")]
    public async Task<IActionResult> Shelf()
    {
        var progress = await db.ReadingProgress.AsNoTracking().Where(x => x.UserId == UserId).OrderByDescending(x => x.UpdatedAt).ToListAsync();
        var ids = progress.Select(x => x.BookId).ToArray();
        var books = await CatalogProjection.Books(db, db.Books.Where(b => ids.Contains(b.BookId))).ToDictionaryAsync(b => b.BookId);
        return Ok(progress.Select(p => new { book = books[p.BookId], p.Status, p.CurrentPage, p.FurthestPage, p.TotalPages, p.CurrentChapter, p.FurthestChapter, p.Location, p.FinishedAt }));
    }

    [HttpPut("shelf/{bookId:int}")]
    public async Task<IActionResult> Progress(int bookId, ProgressDto input)
    {
        var book = await db.Books.AsNoTracking().SingleOrDefaultAsync(x => x.BookId == bookId && x.IsActive);
        if (book == null) return NotFound();
        if (input.TotalPages > 0 && input.CurrentPage > input.TotalPages) return BadRequest(new { message = "Page exceeds the document length." });
        if (book.ChapterCount > 0 && input.CurrentChapter > book.ChapterCount) return BadRequest(new { message = "Chapter exceeds the book length." });
        var progress = await db.ReadingProgress.SingleOrDefaultAsync(x => x.UserId == UserId && x.BookId == bookId);
        if (progress == null) { progress = new ReadingProgress { UserId = UserId, BookId = bookId }; db.ReadingProgress.Add(progress); }
        var pagesRead = input.Status == "want" ? 0 : Math.Max(0, input.CurrentPage - progress.FurthestPage);
        progress.Status = input.Status; progress.CurrentPage = input.CurrentPage; progress.TotalPages = input.TotalPages;
        progress.CurrentChapter = input.CurrentChapter; progress.Location = input.Location;
        if (input.Status != "want") { progress.FurthestPage = Math.Max(progress.FurthestPage, input.CurrentPage); progress.FurthestChapter = Math.Max(progress.FurthestChapter, input.CurrentChapter); }
        progress.UpdatedAt = DateTime.UtcNow;
        progress.FinishedAt = input.Status == "finished" ? progress.FinishedAt ?? DateTime.UtcNow : null;
        if (pagesRead > 0)
        {
            var today = DateTime.UtcNow.Date;
            var daily = await db.ReadingEvents.SingleOrDefaultAsync(x => x.UserId == UserId && x.BookId == bookId && x.Day == today);
            if (daily == null) { daily = new ReadingEvent { UserId = UserId, BookId = bookId, Day = today }; db.ReadingEvents.Add(daily); }
            daily.PagesRead += Math.Min(pagesRead, 1000);
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("shelf/{bookId:int}")]
    public async Task<IActionResult> Remove(int bookId) { await db.ReadingProgress.Where(x => x.UserId == UserId && x.BookId == bookId).ExecuteDeleteAsync(); return NoContent(); }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var items = db.ReadingProgress.Where(x => x.UserId == UserId);
        var events = db.ReadingEvents.Where(x => x.UserId == UserId);
        var year = new DateTime(DateTime.UtcNow.Year, 1, 1);
        return Ok(new { saved = await items.CountAsync(), reading = await items.CountAsync(x => x.Status == "reading"), finished = await items.CountAsync(x => x.Status == "finished"), finishedThisYear = await items.CountAsync(x => x.FinishedAt >= year), pagesRead = await events.SumAsync(x => (int?)x.PagesRead) ?? 0, activeDays = await events.Select(x => x.Day).Distinct().CountAsync(), journals = await db.Journals.CountAsync(x => x.UserId == UserId) });
    }

    [HttpGet("bookmarks")]
    public async Task<IActionResult> Bookmarks([FromQuery] int bookId) => Ok(await db.Bookmarks.AsNoTracking().Where(x => x.UserId == UserId && x.BookId == bookId).OrderBy(x => x.Page).Select(x => new { x.Id, x.Page, x.Chapter, x.Location, x.Label }).ToListAsync());

    [HttpPost("bookmarks/{bookId:int}")]
    public async Task<IActionResult> AddBookmark(int bookId, BookmarkDto input)
    {
        if (!await db.Books.AnyAsync(x => x.BookId == bookId && x.IsActive)) return NotFound();
        var bookmark = new Bookmark { UserId = UserId, BookId = bookId, Page = input.Page, Chapter = input.Chapter, Location = input.Location, Label = input.Label.Trim() };
        db.Bookmarks.Add(bookmark); await db.SaveChangesAsync();
        return Ok(new { bookmark.Id });
    }

    [HttpDelete("bookmarks/{id:int}")]
    public async Task<IActionResult> DeleteBookmark(int id) { await db.Bookmarks.Where(x => x.Id == id && x.UserId == UserId).ExecuteDeleteAsync(); return NoContent(); }

    [HttpGet("notes")]
    public async Task<IActionResult> Notes()
    {
        var user = (await users.GetUserAsync(User))!;
        var progress = await db.ReadingProgress.Where(x => x.UserId == UserId).ToDictionaryAsync(x => x.BookId);
        var notes = await db.Notes.AsNoTracking().Include(x => x.Book).Where(x => x.UserId == UserId).OrderByDescending(x => x.CreatedAt).ToListAsync();
        return Ok(notes.Select(note => {
            var state = note.UnlockAt > DateTime.UtcNow ? "sealed" : user.SpoilerShield && note.Page > (progress.GetValueOrDefault(note.BookId)?.FurthestPage ?? 0) ? "spoiler" : "open";
            return new { note.Id, note.BookId, bookTitle = note.Book.Title, body = state == "open" ? note.Body : null, note.Page, note.UnlockAt, note.CreatedAt, state };
        }));
    }

    [HttpPost("notes")]
    public async Task<IActionResult> AddNote(NoteDto input)
    {
        if (input.UnlockAt.HasValue && input.UnlockAt.Value.ToUniversalTime() <= DateTime.UtcNow) return BadRequest(new { message = "Capsules must open in the future." });
        if (!await db.Books.AnyAsync(x => x.BookId == input.BookId && x.IsActive)) return NotFound();
        var note = new MarginalNote { UserId = UserId, BookId = input.BookId, Body = input.Body.Trim(), Page = input.Page, UnlockAt = input.UnlockAt?.ToUniversalTime() };
        db.Notes.Add(note); await db.SaveChangesAsync(); return Ok(new { note.Id });
    }

    [HttpDelete("notes/{id:int}")]
    public async Task<IActionResult> DeleteNote(int id) { await db.Notes.Where(x => x.Id == id && x.UserId == UserId).ExecuteDeleteAsync(); return NoContent(); }

    [HttpGet("journals")]
    public async Task<IActionResult> Journals([FromQuery] int page = 1) => Ok(await db.Journals.AsNoTracking().Where(x => x.UserId == UserId).OrderByDescending(x => x.CreatedAt).Skip((Math.Max(1, page) - 1) * 20).Take(20).Select(x => new { x.Id, x.Content, x.ExtractedMood, x.RecommendationSummary, x.CreatedAt }).ToListAsync());

    [HttpDelete("journals/{id:int}")]
    public async Task<IActionResult> DeleteJournal(int id) { await db.Journals.Where(x => x.Id == id && x.UserId == UserId).ExecuteDeleteAsync(); return NoContent(); }
}
