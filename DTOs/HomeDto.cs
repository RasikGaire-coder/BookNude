using System.Collections.Generic;

namespace Folio.Api.DTOs
{
    public class HomeDto
    {
        public List<BookDto> FeaturedBooks { get; set; } = new();
        public List<BookDto> StaffPicks { get; set; } = new();
        public List<BookDto> TopTenBooks { get; set; } = new();
        public List<GenreDto> Genres { get; set; } = new();
    }
}

