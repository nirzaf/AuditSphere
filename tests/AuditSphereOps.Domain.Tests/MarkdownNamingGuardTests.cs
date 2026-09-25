using System.Text.RegularExpressions;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// Architecture guard enforcing the Markdown documentation naming policy
/// (docs/architecture/auditsphere-architecture-document-naming-policy.md):
/// Every markdown file must have a globally unique, deterministic, business-semantic
/// basename adhering to auditsphere-<area>-<document-type>-<subject>[-<id>][-<status>].md,
/// with the sole conventional exceptions of root README.md and AGENTS.md.
/// </summary>
public sealed class MarkdownNamingGuardTests
{
  private static readonly HashSet<string> ConventionalExceptions = new(StringComparer.Ordinal)
  {
    "README.md",
    "AGENTS.md"
  };

  private static readonly HashSet<string> ProhibitedGenericNames = new(StringComparer.OrdinalIgnoreCase)
  {
    "new.md", "final.md", "misc.md", "notes.md", "document.md", "file.md",
    "temp.md", "tmp.md", "test.md", "doc.md", "task.md", "spec.md"
  };

  private static string FindRepoRoot()
  {
    var current = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(current))
    {
      if (File.Exists(Path.Combine(current, "AuditSphereOps.slnx")))
      {
        return current;
      }
      var parent = Directory.GetParent(current);
      if (parent is null) break;
      current = parent.FullName;
    }
    throw new InvalidOperationException("Could not locate repository root containing AuditSphereOps.slnx");
  }

  private static List<FileInfo> GetTrackedMarkdownFiles()
  {
    var root = FindRepoRoot();
    try
    {
      using var proc = new System.Diagnostics.Process();
      proc.StartInfo = new System.Diagnostics.ProcessStartInfo
      {
        FileName = "git",
        Arguments = "ls-files \"*.md\"",
        WorkingDirectory = root,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };
      if (proc.Start())
      {
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
        {
          var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
          return lines.Select(l => new FileInfo(Path.Combine(root, l.Trim()))).Where(f => f.Exists).ToList();
        }
      }
    }
    catch
    {
      // Fallback to directory walk below if git is unavailable
    }

    var ignoredPrefixes = new[]
    {
      "bin/", "obj/", ".git/", "tmp/", "graphify-out/", ".graphify",
      "App_Data/", "pbc-staging/", "pbc-provider-simulation/",
      "release-checkpoints/", ".vs/", ".vscode/", ".idea/", "artifacts/",
      ".qoder/", ".claude/", "node_modules/"
    };

    var allMdFiles = Directory.GetFiles(root, "*.md", SearchOption.AllDirectories)
      .Select(p => new FileInfo(p))
      .Where(f =>
      {
        var rel = Path.GetRelativePath(root, f.FullName).Replace('\\', '/');
        return !ignoredPrefixes.Any(p => rel.StartsWith(p, StringComparison.OrdinalIgnoreCase) || rel.Contains("/" + p, StringComparison.OrdinalIgnoreCase));
      })
      .ToList();

    return allMdFiles;
  }

  [Fact(DisplayName = "All markdown files have globally unique basenames")]
  [Trait("Profile", "Architecture")]
  public void AllMarkdownFilesHaveGloballyUniqueBasenames()
  {
    var files = GetTrackedMarkdownFiles();
    Assert.True(files.Count >= 100, $"Expected at least 100 markdown files, but discovered {files.Count}");

    var duplicates = files
      .GroupBy(f => f.Name, StringComparer.Ordinal)
      .Where(g => g.Count() > 1)
      .Select(g => $"{g.Key} ({g.Count()} occurrences: {string.Join(", ", g.Select(f => f.FullName))})")
      .ToList();

    Assert.Empty(duplicates);
  }

  [Fact(DisplayName = "All markdown filenames conform to the naming policy")]
  [Trait("Profile", "Architecture")]
  public void AllMarkdownFilenamesConformToPolicy()
  {
    var root = FindRepoRoot();
    var files = GetTrackedMarkdownFiles();
    var errors = new List<string>();

    foreach (var file in files)
    {
      var relPath = Path.GetRelativePath(root, file.FullName).Replace('\\', '/');
      var name = file.Name;

      if (ConventionalExceptions.Contains(name))
      {
        if (relPath != name)
        {
          errors.Add($"Conventional exception '{name}' is only permitted at the repository root, found at: {relPath}");
        }
        continue;
      }

      if (!name.EndsWith(".md", StringComparison.Ordinal))
      {
        errors.Add($"File must end with '.md': {relPath}");
      }

      if (ProhibitedGenericNames.Contains(name))
      {
        errors.Add($"Prohibited generic name '{name}' at: {relPath}");
      }

      if (name.Contains(' '))
      {
        errors.Add($"Filename contains spaces: {relPath}");
      }

      if (name.Contains('_'))
      {
        errors.Add($"Filename contains underscores: {relPath}");
      }

      if (name.Any(char.IsUpper))
      {
        errors.Add($"Filename contains uppercase characters: {relPath}");
      }

      if (!name.StartsWith("auditsphere-", StringComparison.Ordinal))
      {
        errors.Add($"Filename must begin with 'auditsphere-' prefix: {relPath}");
      }

      var nameWithoutExt = name[..^3];
      if (!Regex.IsMatch(nameWithoutExt, @"^[a-z0-9]+(?:-[a-z0-9]+)*$"))
      {
        errors.Add($"Filename '{name}' does not follow lowercase kebab-case format: {relPath}");
      }
    }

    Assert.Empty(errors);
  }
}
