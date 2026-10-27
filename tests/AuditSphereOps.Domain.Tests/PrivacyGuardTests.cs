using System.Text.Json;
using System.Text.RegularExpressions;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// ASPM-NFR-02 guards: confidential values must not reach logs, traces or error responses through configuration or a
/// stray log statement. Source-scanning assertions, like the architecture guards, so they run without a host.
/// </summary>
public sealed partial class PrivacyGuardTests
{
  private static readonly string Root = FindRepoRoot();

  private static IEnumerable<(string Path, string Text)> SourceFiles() =>
    Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
      .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
        !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
      .Select(path => (Path.GetRelativePath(Root, path), File.ReadAllText(path)));

  [Fact(DisplayName = "No host enables EF sensitive-data logging, detailed errors or request-body logging")]
  [Trait("Profile", "Architecture")]
  public void NoHostLogsParameterValuesOrBodies()
  {
    string[] forbidden = ["EnableSensitiveDataLogging", "EnableDetailedErrors", "UseSerilogRequestLogging", "UseDeveloperExceptionPage",
      "SetDbStatementForText", "IncludeFormattedMessage"];
    var hits = SourceFiles().SelectMany(f => forbidden.Where(f.Text.Contains).Select(term => $"{f.Path}: {term}")).ToList();
    Assert.Empty(hits);
  }

  [Fact(DisplayName = "No log message template names a confidential value")]
  [Trait("Profile", "Architecture")]
  public void NoLogTemplateNamesAConfidentialValue()
  {
    var hits = SourceFiles().SelectMany(f => LogTemplate().Matches(f.Text)
        .Where(m => ConfidentialPlaceholder().IsMatch(m.Groups["template"].Value))
        .Select(m => $"{f.Path}: {m.Groups["template"].Value}"))
      .ToList();
    Assert.Empty(hits);
  }

  [Theory(DisplayName = "Hosts log EF commands only at Warning or above")]
  [Trait("Profile", "Architecture")]
  [InlineData("src/AuditSphereOps.Api/appsettings.json")]
  [InlineData("src/AuditSphereOps.Worker/appsettings.json")]
  public void HostsDoNotLogSqlCommandsAtInformation(string relativePath)
  {
    using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, relativePath)));
    var level = settings.RootElement.GetProperty("Serilog").GetProperty("MinimumLevel").GetProperty("Override")
      .GetProperty("Microsoft.EntityFrameworkCore.Database.Command").GetString();
    Assert.Contains(level, new[] { "Warning", "Error", "Fatal" });
  }

  [Fact(DisplayName = "Production API errors return a generic message with a diagnostic ID")]
  [Trait("Profile", "Architecture")]
  public void ProductionErrorsHideExceptionDetail()
  {
    var host = File.ReadAllText(Path.Combine(Root, "src/AuditSphereOps.Api/ApiHost.cs"));
    Assert.Contains("if (!app.Environment.IsDevelopment())", host, StringComparison.Ordinal);
    Assert.Contains("The request could not be completed. Use the diagnostic ID when contacting your administrator.", host,
      StringComparison.Ordinal);
    Assert.DoesNotContain("exception.Message", host, StringComparison.Ordinal);
  }

  [GeneratedRegex("""Log(?:Trace|Debug|Information|Warning|Error|Critical)\(\s*(?:[^,"]*,\s*)?(?:[^,"]*,\s*)?\$?"(?<template>[^"]*)""")]
  private static partial Regex LogTemplate();

  [GeneratedRegex(@"\{(?:\w*(?:Password|Secret|Token|Email|Iban|Body|Narrative|Salary|CostRate)\w*)\}", RegexOptions.IgnoreCase)]
  private static partial Regex ConfidentialPlaceholder();

  private static string FindRepoRoot()
  {
    var current = AppContext.BaseDirectory;
    while (!File.Exists(Path.Combine(current, "AuditSphereOps.slnx")))
      current = Directory.GetParent(current)?.FullName ??
        throw new InvalidOperationException("Could not locate repository root containing AuditSphereOps.slnx");
    return current;
  }
}
