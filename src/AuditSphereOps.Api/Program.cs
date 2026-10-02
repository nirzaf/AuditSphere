namespace AuditSphereOps.Api;

public partial class ApiProgram
{
  public static void Main(string[] args) => ApiHost.Create(args).Run();
}
