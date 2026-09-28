using AuditSphereOps.Web.Services;

namespace AuditSphereOps.Api.Tests;

public sealed class ProjectProgressReaderTests
{
  [Fact]
  public void PublishedTrackerCoversEveryModuleAndKeepsTaskCountsDistinct()
  {
    var directory = Path.Combine(Path.GetDirectoryName(typeof(ProjectProgressReader).Assembly.Location)!, "project-progress");
    var progress = ProjectProgressReader.Read(directory);
    Assert.Equal(75, progress.Tasks.Count);
    Assert.Equal(Enumerable.Range(20, 7), progress.Modules.Select(x => x.Number));
    Assert.All(progress.Modules, module => Assert.NotEmpty(module.Tasks));
    Assert.NotEmpty(progress.AuditTasks);
    Assert.NotEmpty(progress.SharedTasks);
    Assert.Equal(progress.Tasks.Count, progress.Completed + progress.Active + progress.Blocked + progress.Pending);
  }

  [Fact]
  public void CountsEachTaskOnceAndSeparatesSharedCards()
  {
    var root = Path.Combine(Path.GetTempPath(), "auditsphere-progress-" + Guid.NewGuid().ToString("N"));
    try
    {
      Directory.CreateDirectory(Path.Combine(root, "tracking"));
      Directory.CreateDirectory(Path.Combine(root, "tasks"));
      File.WriteAllText(Path.Combine(root, "tracking", "pack_manifest.json"), """
        {"tasks":[
          {"id":"T001","file":"tasks/one.md","work_package":"R2R-00","module_ids":[20,21]},
          {"id":"T002","file":"tasks/two.md","work_package":"R2R-01","module_ids":[20]},
          {"id":"T003","file":"tasks/three.md","work_package":"R2R-02","module_ids":[]}
        ]}
        """);
      File.WriteAllText(Path.Combine(root, "tasks", "one.md"), "---\nid: \"T001\"\nstatus: \"COMPLETED\"\n---\n# First task\n");
      File.WriteAllText(Path.Combine(root, "tasks", "two.md"), "---\nid: \"T002\"\nstatus: \"BLOCKED\"\nblocked_reason: \"Review needed\"\n---\n# Second task\n");
      File.WriteAllText(Path.Combine(root, "tasks", "three.md"), "---\nid: \"T003\"\nstatus: \"IN_PROGRESS\"\n---\n# Shared task\n");

      var progress = ProjectProgressReader.Read(root);
      Assert.Equal(3, progress.Tasks.Count);
      Assert.Equal((1, 1, 1, 0), (progress.Completed, progress.Active, progress.Blocked, progress.Pending));
      Assert.Equal((1, 2), (progress.Modules.Single(x => x.Number == 20).Completed,
        progress.Modules.Single(x => x.Number == 20).Tasks.Count));
      Assert.Equal("T003", Assert.Single(progress.SharedTasks).Id);
      Assert.Empty(progress.AuditTasks);
      Assert.Equal("Review needed", progress.Tasks.Single(x => x.Id == "T002").BlockedReason);
    }
    finally { Directory.Delete(root, recursive: true); }
  }

  [Fact]
  public void MissingOrEscapingTaskFilesFailClosed()
  {
    var root = Path.Combine(Path.GetTempPath(), "auditsphere-progress-" + Guid.NewGuid().ToString("N"));
    try
    {
      Directory.CreateDirectory(Path.Combine(root, "tracking"));
      File.WriteAllText(Path.Combine(root, "tracking", "pack_manifest.json"),
        """{"tasks":[{"id":"T001","file":"../outside.md","work_package":"R2R-00","module_ids":[20]}]}""");
      Assert.Throws<InvalidDataException>(() => ProjectProgressReader.Read(root));
    }
    finally { Directory.Delete(root, recursive: true); }
  }
}
