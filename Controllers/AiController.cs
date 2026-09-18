using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BookNerd.Domain.Services;
using BookNerd.Infrastructure.Ai;
using Folio.Api.Data;
using Folio.Api.Models;
using Folio.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Folio.Api.Controllers;

public record DetectiveInput([Required, StringLength(2000, MinimumLength = 5)] string Scene);
public record MoodInput([Required, StringLength(5000, MinimumLength = 5)] string Journal, bool Save = true);

[ApiController, Route("api/ai"), Authorize, EnableRateLimiting("ai")]
public class AiController(FolioDbContext db, OpenAiService ai) : ControllerBase
{
    [HttpPost("detective")]
    public async Task<IActionResult> Detective(DetectiveInput input, CancellationToken ct)
    {
        if (!ai.Configured) return StatusCode(503, new { message = "AI search is not configured. Add the server's OpenAI API key to enable scene matching." });
        return Ok(new { method = "embedding-cosine", confidenceNotice = "Similarity scores measure catalog relevance, not certainty that this is the remembered book.", items = await Search(input.Scene, ct) });
    }

    [HttpPost("mood-recommend")]
    public async Task<IActionResult> Mood(MoodInput input, CancellationToken ct)
    {
        if (!ai.Configured) return StatusCode(503, new { message = "Mood recommendations need the server's OpenAI API key." });
        var analysis = await ai.AnalyzeMoodAsync(input.Journal, ct);
        var matches = await Search($"{analysis.Mood}. {string.Join(", ", analysis.Keywords)}. {analysis.Summary}", ct);
        if (input.Save) { db.Journals.Add(new JournalEntry { UserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!, Content = input.Journal, ExtractedMood = analysis.Mood[..Math.Min(100, analysis.Mood.Length)], RecommendationSummary = analysis.Summary[..Math.Min(2000, analysis.Summary.Length)] }); await db.SaveChangesAsync(ct); }
        return Ok(new { analysis.Mood, analysis.Keywords, analysis.Summary, saved = input.Save, items = matches });
    }

    private async Task<object[]> Search(string text, CancellationToken ct)
    {
        var index = await db.BookEmbeddings.AsNoTracking().Where(e => e.Book.IsActive && e.Model == ai.EmbeddingModel).ToListAsync(ct);
        if (index.Count == 0) throw new AiUnavailableException("The semantic catalog index is empty. An administrator must index the books first.");
        var query = (await ai.EmbedAsync([text], ct))[0];
        var scored = index.Select(e => new { e.BookId, score = RecommendationMath.Cosine(query, JsonSerializer.Deserialize<float[]>(e.VectorJson)!) }).OrderByDescending(e => e.score).Take(8).Where(e => e.score > 0.15).ToList();
        var ids = scored.Select(x => x.BookId).ToArray();
        var books = await CatalogProjection.Books(db, db.Books.Where(b => ids.Contains(b.BookId))).ToDictionaryAsync(b => b.BookId, ct);
        return scored.Select(x => (object)new { book = books[x.BookId], similarity = Math.Round(x.score, 3), confidence = Math.Round(Math.Max(0, x.score) * 100, 1) }).ToArray();
    }

    [Authorize(Roles = "Admin"), HttpPost("reindex")]
    public async Task<IActionResult> Reindex([FromQuery, Range(1, 100000)] int page = 1, CancellationToken ct = default)
    {
        if (!ai.Configured) return StatusCode(503, new { message = "Configure OpenAI:ApiKey first." });
        var books = await db.Books.Include(b => b.StoryTags).Where(b => b.IsActive).OrderBy(b => b.BookId).Skip((page - 1) * 50).Take(50).ToListAsync(ct);
        var ids = books.Select(b => b.BookId).ToArray();
        var existing = await db.BookEmbeddings.Where(e => ids.Contains(e.BookId)).ToDictionaryAsync(e => e.BookId, ct);
        var prepared = books.Select(b => { var text = $"{b.Title} by {b.Author}. {b.Description}. Genre: {b.GenreType}. Narrative: {string.Join(", ", b.StoryTags.Select(t => t.Name))}"; return new { book = b, text, hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))) }; }).Where(x => !existing.TryGetValue(x.book.BookId, out var old) || old.ContentHash != x.hash || old.Model != ai.EmbeddingModel).ToList();
        if (prepared.Count > 0)
        {
            var vectors = await ai.EmbedAsync(prepared.Select(x => x.text).ToArray(), ct);
            for (var i = 0; i < prepared.Count; i++) {
                var item = prepared[i];
                if (!existing.TryGetValue(item.book.BookId, out var embedding)) { embedding = new BookEmbedding { BookId = item.book.BookId }; db.BookEmbeddings.Add(embedding); }
                embedding.Model = ai.EmbeddingModel; embedding.ContentHash = item.hash; embedding.VectorJson = JsonSerializer.Serialize(vectors[i]); embedding.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(ct);
        }
        return Ok(new { indexed = prepared.Count, scanned = books.Count, nextPage = books.Count == 50 ? page + 1 : (int?)null });
    }
}
