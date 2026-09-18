using System;
using Folio.Api.Models;

namespace Folio.Api.DTOs
{
    /// <summary>
    /// Read-only response shape for a book.
    /// </summary>
    public class BookDto
    {
        public int BookId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string? Description { get; set; }

        // Relational genre
        public int GenreId { get; set; }
        public string? GenreName { get; set; }

        // Enum genre — lets the client work with a strongly-typed genre value
        public BookGenre GenreType { get; set; }

        public string? CoverColor { get; set; }
        public string? Badge { get; set; }

        public decimal AverageRating { get; set; }
        public int ReviewCount { get; set; }
        public bool IsNew { get; set; }
        public bool IsTrending { get; set; }

        public DateTime PublishedDate { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public string? Isbn { get; set; }
        public string? CoverUrl { get; set; }
        public bool IsFree { get; set; }
        public string? FileFormat { get; set; }
        public string? License { get; set; }
        public bool CanRead { get; set; }
        public int ReadTimeMinutes { get; set; }
        public int ChapterCount { get; set; }
        public List<string> StoryStructureTags { get; set; } = [];
    }
}
