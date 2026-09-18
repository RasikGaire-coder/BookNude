using System;
using System.ComponentModel.DataAnnotations;
using Folio.Api.Models;

namespace Folio.Api.DTOs
{
    /// <summary>
    /// Payload for PUT /api/books/{id} — updating an existing book.
    /// </summary>
    public class UpdateBookDto
    {
        [Required(ErrorMessage = "Title is required.")]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Author is required.")]
        [StringLength(100)]
        public string Author { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Description { get; set; }

        /// <summary>
        /// Genre selected from the BookGenre enum.
        /// Updating the genre also syncs GenreId automatically.
        /// </summary>
        [Required(ErrorMessage = "GenreType is required.")]
        public BookGenre GenreType { get; set; }

        [StringLength(50)]
        public string? CoverColor { get; set; }

        [StringLength(20)]
        public string? Badge { get; set; }

        [Required]
        public DateTime PublishedDate { get; set; }

        public bool IsActive { get; set; }
    }
}
