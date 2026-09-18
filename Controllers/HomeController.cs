using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Folio.Api.Data;
using Folio.Api.DTOs;
using System.Linq;


namespace Folio.Api.Controllers
{
    [ApiController]
    [Route("api/home")]
    public class HomeController : ControllerBase
    {
        private readonly FolioDbContext _context;

        public HomeController(FolioDbContext context)
        {
            _context = context;
        }

        // GET: api/home
        [HttpGet]
        public async Task<ActionResult<HomeDto>> GetHome()
        {
            var dto = new HomeDto
            {
                FeaturedBooks = await _context.Books
                    .Where(b => b.IsActive)
                    .Take(8)
                    .Select(b => new BookDto
                    {
                        BookId = b.BookId,
                        Title = b.Title,
                        Author = b.Author,
                        Description = b.Description,
                        GenreId = b.GenreId,
                        GenreName = b.Genre.Name,
                        CoverColor = b.CoverColor,
                        Badge = b.Badge,
                        AverageRating = b.Rating != null ? b.Rating.AverageScore : 0,
                        ReviewCount = b.Rating != null ? b.Rating.ReviewCount : 0,
                        IsNew = b.IsNew,
                        IsTrending = b.IsTrending,
                        PublishedDate = b.PublishedDate,
                        IsActive = b.IsActive,
                        CreatedDate = b.CreatedDate
                    })
                    .ToListAsync(),

                StaffPicks = await _context.Books
                    .Where(b => b.IsActive && b.Badge == "New")
                    .Take(6)
                    .Select(b => new BookDto
                    {
                        BookId = b.BookId,
                        Title = b.Title,
                        Author = b.Author,
                        Description = b.Description,
                        GenreId = b.GenreId,
                        GenreName = b.Genre.Name,
                        CoverColor = b.CoverColor,
                        Badge = b.Badge,
                        AverageRating = b.Rating != null ? b.Rating.AverageScore : 0,
                        ReviewCount = b.Rating != null ? b.Rating.ReviewCount : 0,
                        IsNew = b.IsNew,
                        IsTrending = b.IsTrending,
                        PublishedDate = b.PublishedDate,
                        IsActive = b.IsActive,
                        CreatedDate = b.CreatedDate
                    })
                    .ToListAsync(),

                TopTenBooks = await _context.Books
                    .Where(b => b.IsActive && b.Rating != null)
                    .OrderByDescending(b => b.Rating.AverageScore)
                    .Take(10)
                    .Select(b => new BookDto
                    {
                        BookId = b.BookId,
                        Title = b.Title,
                        Author = b.Author,
                        Description = b.Description,
                        GenreId = b.GenreId,
                        GenreName = b.Genre.Name,
                        CoverColor = b.CoverColor,
                        Badge = b.Badge,
                        AverageRating = b.Rating != null ? b.Rating.AverageScore : 0,
                        ReviewCount = b.Rating != null ? b.Rating.ReviewCount : 0,
                        IsNew = b.IsNew,
                        IsTrending = b.IsTrending,
                        PublishedDate = b.PublishedDate,
                        IsActive = b.IsActive,
                        CreatedDate = b.CreatedDate
                    })
                    .ToListAsync(),

                Genres = await _context.Genres
                    .OrderBy(g => g.Name)
                    .Select(g => new GenreDto
                    {
                        GenreId = g.GenreId,
                        Name = g.Name,
                        Description = g.Description
                    })
                    .ToListAsync()
            };

            return Ok(dto);
        }
    }
}
