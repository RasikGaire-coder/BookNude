using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Folio.Api.Data;
using Folio.Api.DTOs;

namespace Folio.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GenresController : ControllerBase
    {
        private readonly FolioDbContext _context;

        public GenresController(FolioDbContext context) => _context = context;

        // GET: api/genres
        [HttpGet]
        public async Task<ActionResult<IEnumerable<GenreDto>>> GetGenres()
        {
            var genres = await _context.Genres
                .OrderBy(g => g.Name)
                .Select(g => new GenreDto
                {
                    GenreId = g.GenreId,
                    Name = g.Name,
                    Description = g.Description
                })
                .ToListAsync();

            return Ok(genres);
        }

        // GET: api/genres/2/books
        [HttpGet("{id:int}/books")]
        public async Task<ActionResult<IEnumerable<BookDto>>> GetBooksByGenre(int id)
        {
            if (!await _context.Genres.AnyAsync(g => g.GenreId == id))
                return NotFound(new { message = $"Genre {id} was not found." });

            var books = await _context.Books
                .Where(b => b.IsActive && b.GenreId == id)
                .Select(b => new BookDto
                {
                    BookId = b.BookId,
                    Title = b.Title,
                    Author = b.Author,
                    Description = b.Description,
                    GenreId = b.GenreId,
                    CoverColor = b.CoverColor,
                    Badge = b.Badge,
                   
                    PublishedDate = b.PublishedDate,
                   
                    
                    AverageRating = b.AverageRating,
                    IsNew = b.IsNew,
                    IsTrending = b.IsTrending
                })
                .ToListAsync();

            return Ok(books);
        }
    }
}
