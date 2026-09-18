using System.IO.Compression;
using System.Text;
using BookNerd.Domain.Services;
using Folio.Api.Data;
using Folio.Api.DTOs;
using Folio.Api.Models;
using Folio.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Folio.Api.Controllers;

[ApiController, Route("api/books")]
public class BooksController(FolioDbContext db, IFileStorage storage) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] CatalogQuery input, CancellationToken ct)
    {
        var books = db.Books.AsNoTracking().Where(b => b.IsActive);
        if (input.Genre.HasValue) books = books.Where(b => b.GenreType == input.Genre);
        if (input.Genres.Length > 0) books = books.Where(b => input.Genres.Contains(b.GenreId));
        if (!string.IsNullOrWhiteSpace(input.Search)) { var term = input.Search.Trim(); books = books.Where(b => b.Title.Contains(term) || b.Author.Contains(term) || (b.Isbn != null && b.Isbn.Contains(term))); }
        if (input.IsFree.HasValue) books = books.Where(b => b.IsFree == input.IsFree);
        if (input.PublishedFrom.HasValue) books = books.Where(b => b.PublishedDate >= input.PublishedFrom);
        if (input.PublishedTo.HasValue) books = books.Where(b => b.PublishedDate <= input.PublishedTo);
        if (input.MaxReadTime.HasValue) books = books.Where(b => b.ReadTimeMinutes > 0 && b.ReadTimeMinutes <= input.MaxReadTime);
        var query = CatalogProjection.Books(db, books);
        if (input.MinRating.HasValue) query = query.Where(b => b.AverageRating >= (decimal)input.MinRating);
        var total = await query.CountAsync(ct);
        query = input.Sort switch { "title" => query.OrderBy(b => b.Title).ThenBy(b => b.BookId), "newest" => query.OrderByDescending(b => b.CreatedDate).ThenBy(b => b.BookId), _ => query.OrderByDescending(b => b.AverageRating).ThenBy(b => b.BookId) };
        return Ok(new { items = await query.Skip((input.Page - 1) * input.PageSize).Take(input.PageSize).ToListAsync(ct), input.Page, input.PageSize, totalItems = total, totalPages = (int)Math.Ceiling(total / (double)input.PageSize) });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id) { var book = await CatalogProjection.Books(db, db.Books.Where(b => b.BookId == id && b.IsActive)).SingleOrDefaultAsync(); return book == null ? NotFound() : Ok(book); }

    [HttpGet("genres")]
    public IActionResult Genres() => Ok(Enum.GetValues<BookGenre>().Select(g => new { value = (int)g, name = g.ToString(), genreId = (int)g }));

    [HttpGet("featured")]
    public async Task<IActionResult> Featured() => Ok(await CatalogProjection.Books(db, db.Books.Where(b => b.IsActive)).OrderByDescending(b => b.CanRead).ThenByDescending(b => b.AverageRating).Take(8).ToListAsync());

    [HttpGet("top-ten")]
    public async Task<IActionResult> TopTen() => Ok(await CatalogProjection.Books(db, db.Books.Where(b => b.IsActive)).OrderByDescending(b => b.AverageRating).Take(10).ToListAsync());

    [HttpGet("{id:int}/read")]
    public async Task<IActionResult> Read(int id, CancellationToken ct)
    {
        var book = await db.Books.AsNoTracking().SingleOrDefaultAsync(b => b.BookId == id && b.IsActive, ct);
        if (book == null) return NotFound();
        if (!book.IsFree || string.IsNullOrWhiteSpace(book.License)) return StatusCode(403, new { message = "This book is not licensed for free reading." });
        if (book.FileKey == null) return NotFound(new { message = "No reading file has been uploaded." });
        var stream = await storage.OpenAsync(book.FileKey, ct);
        if (stream == null) return NotFound(new { message = "The reading file could not be found." });
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(stream, book.FileFormat == "epub" ? "application/epub+zip" : book.FileFormat == "pdf" ? "application/pdf" : "text/plain; charset=utf-8", enableRangeProcessing: true);
    }

    [HttpGet("{id:int}/cover")]
    public async Task<IActionResult> Cover(int id, CancellationToken ct)
    {
        var key = await db.Books.Where(b => b.BookId == id && b.IsActive).Select(b => b.CoverKey).SingleOrDefaultAsync(ct);
        if (key == null) return NotFound();
        var stream = await storage.OpenAsync(key, ct);
        return stream == null ? NotFound() : File(stream, ImageType(key));
    }

    [Authorize(Roles = "Admin"), HttpPost]
    public async Task<IActionResult> Create(SaveBookDto input)
    {
        if (!Valid(input)) return ValidationProblem(ModelState);
        var book = new Book { CreatedDate = DateTime.UtcNow };
        Apply(book, input); db.Books.Add(book); await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Detail), new { id = book.BookId }, new { book.BookId });
    }

    [Authorize(Roles = "Admin"), HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveBookDto input)
    {
        if (!Valid(input)) return ValidationProblem(ModelState);
        var book = await db.Books.Include(x => x.StoryTags).SingleOrDefaultAsync(b => b.BookId == id);
        if (book == null) return NotFound();
        var desired = input.StoryStructureTags.Distinct().ToHashSet();
        db.StoryTags.RemoveRange(book.StoryTags.Where(t => !desired.Contains(t.Name)));
        foreach (var tag in desired.Where(t => book.StoryTags.All(old => old.Name != t))) book.StoryTags.Add(new StoryTag { Name = tag });
        Apply(book, input, false); await db.SaveChangesAsync(); return NoContent();
    }

    [Authorize(Roles = "Admin"), HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) { var count = await db.Books.Where(b => b.BookId == id).ExecuteUpdateAsync(set => set.SetProperty(b => b.IsActive, false)); return count == 0 ? NotFound() : NoContent(); }

    [Authorize(Roles = "Admin"), HttpPost("{id:int}/file"), RequestSizeLimit(32_000_000)]
    public async Task<IActionResult> UploadFile(int id, IFormFile file, CancellationToken ct)
    {
        var book = await db.Books.FindAsync([id], ct);
        if (book == null) return NotFound();
        if (file.Length is <= 0 or > 30_000_000) return BadRequest(new { message = "Choose a file up to 30 MB." });
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".pdf" or ".epub" or ".txt")) return BadRequest(new { message = "Use PDF, EPUB, or plain text." });
        await using var buffer = new MemoryStream(); await file.CopyToAsync(buffer, ct); buffer.Position = 0;
        if (extension == ".pdf" && !Encoding.ASCII.GetString(buffer.ToArray().AsSpan(0, (int)Math.Min(buffer.Length, 5))).StartsWith("%PDF-")) return BadRequest(new { message = "Invalid PDF." });
        if (extension == ".epub")
        {
            try {
                using var archive = new ZipArchive(buffer, ZipArchiveMode.Read, true);
                if (archive.Entries.Count > 4096 || archive.Entries.Sum(e => e.Length) > 100_000_000) return BadRequest(new { message = "EPUB expands beyond the permitted size." });
                var mimetype = archive.GetEntry("mimetype");
                if (mimetype == null || mimetype.Length > 100) return BadRequest(new { message = "Invalid EPUB container." });
                using var reader = new StreamReader(mimetype.Open());
                if ((await reader.ReadToEndAsync(ct)).Trim() != "application/epub+zip") return BadRequest(new { message = "Invalid EPUB mimetype." });
            } catch (InvalidDataException) { return BadRequest(new { message = "Invalid EPUB archive." }); }
        }
        if (extension == ".txt" && buffer.ToArray().Contains((byte)0)) return BadRequest(new { message = "Use UTF-8 plain text." });
        buffer.Position = 0;
        var old = book.FileKey;
        book.FileKey = await storage.SaveAsync(buffer, extension, extension == ".epub" ? "application/epub+zip" : extension == ".pdf" ? "application/pdf" : "text/plain", ct);
        book.FileFormat = extension[1..]; await db.SaveChangesAsync(ct);
        if (old != null) await storage.DeleteAsync(old, ct);
        return Ok(new { book.FileFormat });
    }

    [Authorize(Roles = "Admin"), HttpPost("{id:int}/cover"), RequestSizeLimit(2_100_000)]
    public async Task<IActionResult> UploadCover(int id, IFormFile file, CancellationToken ct)
    {
        var book = await db.Books.FindAsync([id], ct); if (book == null) return NotFound();
        var validation = await ValidateImage(file, ct); if (validation == null) return BadRequest(new { message = "Choose a valid PNG, JPEG, or WebP image up to 2 MB." });
        await using var stream = file.OpenReadStream(); var old = book.CoverKey;
        book.CoverKey = await storage.SaveAsync(stream, validation, ImageType(validation), ct); await db.SaveChangesAsync(ct);
        if (old != null) await storage.DeleteAsync(old, ct);
        return Ok(new { coverUrl = $"/api/books/{id}/cover" });
    }

    internal static string ImageType(string key) => Path.GetExtension(key) switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
    internal static async Task<string?> ValidateImage(IFormFile file, CancellationToken ct)
    {
        if (file.Length is < 12 or > 2_000_000) return null;
        var header = new byte[12]; await using var stream = file.OpenReadStream(); await stream.ReadExactlyAsync(header, ct);
        if (header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ".png";
        if (header[0] == 255 && header[1] == 216 && header[2] == 255) return ".jpg";
        if (Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && Encoding.ASCII.GetString(header, 8, 4) == "WEBP") return ".webp";
        return null;
    }

    private bool Valid(SaveBookDto input)
    {
        if (input.IsFree && string.IsNullOrWhiteSpace(input.License)) ModelState.AddModelError("License", "Free books require a license or public domain statement.");
        if (input.StoryStructureTags.Any(t => !NarrativeTags.All.Contains(t))) ModelState.AddModelError("StoryStructureTags", "Choose supported narrative tags.");
        return ModelState.IsValid;
    }
    private static void Apply(Book book, SaveBookDto input, bool addTags = true)
    {
        book.Title = input.Title.Trim(); book.Author = input.Author.Trim(); book.Description = input.Description;
        book.GenreId = (int)input.GenreType; book.GenreType = input.GenreType; book.Isbn = input.Isbn;
        book.PublishedDate = input.PublishedDate; book.IsFree = input.IsFree; book.IsActive = input.IsActive;
        book.License = input.License; book.ReadTimeMinutes = input.ReadTimeMinutes; book.ChapterCount = input.ChapterCount; book.CoverColor = input.CoverColor;
        if (addTags) book.StoryTags = input.StoryStructureTags.Distinct().Select(t => new StoryTag { Name = t }).ToList();
    }
}
