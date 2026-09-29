using AuditSphereOps.Domain.Acceptance;

namespace AuditSphereOps.Application.Acceptance;

public sealed record ChecklistQuestion(
  string Bank, string Code, string Section, string Prompt, string Category, string AnswerType,
  bool RequiresEvidence, string? AdverseAnswer, bool EscalatesOnChange, int SortOrder, string TemplateVersion = "");

public sealed record ChecklistAnswer(string Bank, string Code, string Answer, string? EvidenceReference, DateTimeOffset AnsweredAt, long Revision);

public sealed record ChecklistClearance(string Area, string Status, string? EvidenceReference, DateTimeOffset? ClearedAt);

public sealed record AcceptanceBlocker(string Kind, string Message, string? QuestionCode = null);

/// <summary>
/// Pure acceptance-readiness evaluation. Filling every answer is not enough: answers that need evidence must carry a
/// reference, and an adverse answer (or a change in a key continuance item) needs a cleared specialist review of its
/// area recorded after that answer, with evidence. Nothing here reads a database, a clock or an identity.
/// </summary>
public static class AcceptanceRules
{
  public const string Yes = "YES";
  public const string No = "NO";

  /// <summary>The bank the path answers: the full onboarding bank for a new client, the delta bank for a recurring one.</summary>
  public static string BankFor(string path) => path == AcceptancePaths.Continuance ? "RV" : "CE";

  public static bool IsAdverse(ChecklistQuestion question, ChecklistAnswer? answer)
  {
    if (answer is null || question.AdverseAnswer is null) return false;
    return string.Equals(answer.Answer.Trim(), question.AdverseAnswer, StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>True for a valid boolean answer spelling (yes/no in any case).</summary>
  public static string? NormalizeBoolean(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant() switch
  {
    "YES" or "Y" or "TRUE" => "Yes",
    "NO" or "N" or "FALSE" => "No",
    _ => null
  };

  public static IReadOnlyList<AcceptanceBlocker> Evaluate(
    IReadOnlyList<ChecklistQuestion> questions,
    IReadOnlyDictionary<(string Bank, string Code), ChecklistAnswer> answers,
    IReadOnlyList<ChecklistClearance> clearances)
  {
    var blockers = new List<AcceptanceBlocker>();
    if (questions.Count == 0)
      return [new("no-questions", "No question bank is loaded for this acceptance path.")];

    foreach (var question in questions.OrderBy(x => x.SortOrder))
    {
      if (!answers.TryGetValue((question.Bank, question.Code), out var answer) || string.IsNullOrWhiteSpace(answer.Answer))
      {
        blockers.Add(new("unanswered", $"{question.Code} is unanswered.", question.Code));
        continue;
      }
      if (question.RequiresEvidence && string.IsNullOrWhiteSpace(answer.EvidenceReference))
        blockers.Add(new("evidence-missing", $"{question.Code} needs a supporting evidence reference.", question.Code));
      if (!IsAdverse(question, answer)) continue;
      var cleared = clearances.Any(c => string.Equals(c.Area, question.Category, StringComparison.OrdinalIgnoreCase) &&
        c.Status == "CLEARED" && !string.IsNullOrWhiteSpace(c.EvidenceReference) && c.ClearedAt >= answer.AnsweredAt);
      if (!cleared)
        blockers.Add(new("adverse-uncleared", question.EscalatesOnChange
          ? $"{question.Code} reports a change that needs a cleared {question.Category} review with evidence."
          : $"{question.Code} is an adverse answer; a cleared {question.Category} review with evidence, recorded after the answer, is required.", question.Code));
    }

    foreach (var open in clearances.Where(c => c.Status != "CLEARED"))
      blockers.Add(new("clearance-open", $"The {open.Area} specialist review is {open.Status}."));
    return blockers;
  }
}
