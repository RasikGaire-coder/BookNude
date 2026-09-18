using System.ComponentModel.DataAnnotations;

namespace Folio.Api.DTOs;

public record ProgressDto([Required, RegularExpression("^(want|reading|finished)$")] string Status, [Range(1, 100000)] int CurrentPage, [Range(0, 100000)] int TotalPages, [Range(0, 10000)] int CurrentChapter, [MaxLength(2000)] string? Location);
public record BookmarkDto([Range(1, 100000)] int Page, [Range(0, 10000)] int Chapter, [MaxLength(2000)] string? Location, [Required, MaxLength(100)] string Label);
public record NoteDto([Range(1, int.MaxValue)] int BookId, [Required, MaxLength(3000)] string Body, [Range(1, 100000)] int Page, DateTime? UnlockAt);
public record ReviewDto([Range(1, 5)] int Rating, [Required, StringLength(5000, MinimumLength = 5)] string Comment, bool HasSpoilers, [Range(1, 10000)] int? SpoilerChapter);
public record VoteDto([Range(-1, 1)] int Value);
public record FlagDto([Required, StringLength(500, MinimumLength = 3)] string Reason);
public record ModerateDto([Required, RegularExpression("^(Visible|Hidden)$")] string Status);
