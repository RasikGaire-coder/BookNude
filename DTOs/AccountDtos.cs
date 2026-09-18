using System.ComponentModel.DataAnnotations;

namespace Folio.Api.DTOs;

public record RegisterDto([Required, EmailAddress, MaxLength(254)] string Email, [Required, StringLength(128, MinimumLength = 12)] string Password, [Required, StringLength(80, MinimumLength = 2)] string DisplayName);
public record LoginDto([Required, EmailAddress, MaxLength(254)] string Email, [Required, MaxLength(128)] string Password);
public record ForgotPasswordDto([Required, EmailAddress, MaxLength(254)] string Email);
public record ResetPasswordDto([Required, EmailAddress] string Email, [Required, MaxLength(3000)] string Token, [Required, StringLength(128, MinimumLength = 12)] string Password);
public record ProfileDto([Required, StringLength(80, MinimumLength = 2)] string DisplayName, [Range(1, 1000)] int ReadingGoal, bool SpoilerShield);
