using System.Text.RegularExpressions;

namespace AuditSphereOps.Domain.Security;

/// <summary>
/// Optional employee details that the user maintains for themselves, kept apart from the security identity row. Full name
/// and email stay on <see cref="AppUser"/> because the identity provider supplies them at sign-in.
/// </summary>
public sealed class UserProfile
{
  public Guid UserId { get; set; }
  public Guid FirmId { get; set; }
  public string? Position { get; set; }
  public string? Gender { get; set; }
  public DateOnly? DateOfBirth { get; set; }
  public string? MobileNumber { get; set; }
  public DateOnly? JoinedDate { get; set; }
  public long Revision { get; set; } = 1;
  public DateTimeOffset UpdatedAt { get; set; }
}

public static class UserProfileGenders
{
  public const string Female = "FEMALE";
  public const string Male = "MALE";
  public const string Other = "OTHER";
  public const string NotDisclosed = "PREFER_NOT_TO_SAY";
  public static readonly string[] All = [Female, Male, Other, NotDisclosed];
}

public sealed record UserProfileDetails(string? Position, string? Gender, DateOnly? DateOfBirth, string? MobileNumber,
  DateOnly? JoinedDate);

/// <summary>Pure normalization and validation for self-maintained profile details. Every field is optional.</summary>
public static partial class UserProfileRules
{
  public const int PositionMaxLength = 120;
  public const int MobileMaxLength = 24;
  public static readonly DateOnly EarliestDate = new(1900, 1, 1);

  /// <summary>Trims text, maps blanks to null and returns the first validation failure, or null when valid.</summary>
  public static (UserProfileDetails Details, string? Error) Normalize(UserProfileDetails input, DateOnly today)
  {
    var position = Clean(input.Position);
    var mobile = Clean(input.MobileNumber);
    var gender = Clean(input.Gender)?.ToUpperInvariant();
    var details = new UserProfileDetails(position, gender, input.DateOfBirth, mobile, input.JoinedDate);
    if (position is not null && (position.Length > PositionMaxLength || position.Any(char.IsControl)))
      return (details, $"Position must be {PositionMaxLength} characters or fewer.");
    if (gender is not null && !UserProfileGenders.All.Contains(gender, StringComparer.Ordinal))
      return (details, "Choose a listed gender option or leave it blank.");
    if (input.DateOfBirth is { } birth && (birth < EarliestDate || birth >= today))
      return (details, "Date of birth must be a past date.");
    if (mobile is not null && !ValidMobile(mobile))
      return (details, "Enter a mobile number of 7 to 15 digits, optionally starting with + and the country code.");
    if (input.JoinedDate is { } joined && (joined < EarliestDate || joined > today))
      return (details, "Joined date cannot be in the future.");
    if (input.DateOfBirth is { } born && input.JoinedDate is { } start && start <= born)
      return (details, "Joined date must be after the date of birth.");
    return (details, null);
  }

  private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

  // E.164 allows at most 15 digits; separators are accepted for readability but never stored alone.
  private static bool ValidMobile(string value) =>
    value.Length <= MobileMaxLength && MobilePattern().IsMatch(value) && value.Count(char.IsAsciiDigit) is >= 7 and <= 15;

  [GeneratedRegex(@"^\+?[0-9][0-9 ()-]*[0-9]$", RegexOptions.CultureInvariant)]
  private static partial Regex MobilePattern();
}
