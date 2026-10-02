using AuditSphereOps.Api;
using AuditSphereOps.Web.Authentication;
using MudBlazor.Services;

var app = ApiHost.Create(args,
  addPresentation: builder =>
  {
    builder.Services.AddRazorComponents().AddInteractiveServerComponents();
    builder.Services.AddCascadingAuthenticationState();
    builder.Services.AddMudServices();
    builder.Services.AddScoped<CurrentActorResolver>();
  },
  mapPresentation: app => app.MapRazorComponents<AuditSphereOps.Web.Components.App>().AddInteractiveServerRenderMode(),
  legacyPresentation: true);
app.Run();

public partial class Program { }
