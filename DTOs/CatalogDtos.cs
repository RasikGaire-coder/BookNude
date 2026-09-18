using System.ComponentModel.DataAnnotations;
using Folio.Api.Models;

namespace Folio.Api.DTOs;

public class CatalogQuery
{
    [MaxLength(200)] public string? Search { get; set; }
    public BookGenre? Genre { get; set; }
    public int[] Genres { get; set; } = [];
    [Range(0, 5)] public double? MinRating { get; set; }
    public DateTime? PublishedFrom { get; set; }
    public DateTime? PublishedTo { get; set; }
    [Range(1, 100000)] public int? MaxReadTime { get; set; }
    public bool? IsFree { get; set; }
    [RegularExpression("^(newest|rating|title)$")] public string Sort { get; set; } = "rating";
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 12;
}

public class SaveBookDto
{
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    [Required, MaxLength(100)] public string Author { get; set; } = "";
    [MaxLength(1000)] public string Description { get; set; } = "";
    [EnumDataType(typeof(BookGenre))] public BookGenre GenreType { get; set; }
    [MaxLength(20)] public string? Isbn { get; set; }
    public DateTime PublishedDate { get; set; }
    public bool IsFree { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(100)] public string? License { get; set; }
    [Range(0, 100000)] public int ReadTimeMinutes { get; set; }
    [Range(0, 10000)] public int ChapterCount { get; set; }
    [MaxLength(50)] public string CoverColor { get; set; } = "#244c3d";
    public string[] StoryStructureTags { get; set; } = [];
}
