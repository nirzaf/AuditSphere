using AuditSphereOps.Application.Accounting;
namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapJournalManagementEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/portal/accounting/journals", (int? page, HttpContext http) =>
      ReadAsync(http,(db,actor,ct)=>JournalManagementWorkspace.ClientQueueAsync(db,actor,page ?? 0,ct)));
    MapJournalManagementScope(group,"/portal/accounting/journals/{id:guid}",true);
    MapJournalManagementScope(group,"/accounting/journals/{id:guid}/management",false);
  }
  private static void MapJournalManagementScope(RouteGroupBuilder group,string path,bool client)
  {
    group.MapGet(path,(Guid id,HttpContext http)=>ReadAsync(http,(db,actor,ct)=>JournalManagementWorkspace.GetAsync(db,db,actor,id,client,ct)));
    group.MapGet(path+"/receipts/{requestId:guid}",(Guid id,Guid requestId,string requestHash,HttpContext http)=>
      ReadAsync(http,(db,actor,ct)=>JournalManagementWorkspace.LookupAsync(db,db,actor,id,client,requestId,requestHash,ct)));
    group.MapPost(path+"/preview",(Guid id,JournalManagementRequest? input,HttpContext http)=>
      CommandAsync(http,(db,actor,ct)=>JournalManagementWorkspace.PreviewAsync(db,db,actor,id,client,input,ct)));
    group.MapPost(path,(Guid id,JournalManagementRequest? input,HttpContext http)=>
      CommandAsync(http,(db,actor,ct)=>JournalManagementWorkspace.ExecuteAsync(db,db,actor,id,client,input,ct)));
  }
}
