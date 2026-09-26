using MudBlazor;

namespace AuditSphereOps.Web.Components.Theme;

/// <summary>
/// Central AuditSphere MudBlazor theme (presentation layer only).
/// Professional enterprise tokens aligned with the existing app.css palette
/// (topbar #123047, primary #075985, page #f5f7fa). No business logic here.
/// </summary>
public static class AuditSphereTheme
{
  public static MudTheme Create() => new()
  {
    PaletteLight = new PaletteLight
    {
      Primary = "#075985",
      Secondary = "#0E7490",
      Tertiary = "#123047",
      Info = "#0E7490",
      Success = "#15803D",
      Warning = "#B45309",
      Error = "#B91C1C",
      Dark = "#123047",
      TextPrimary = "#17212B",
      TextSecondary = "#5B6B7A",
      Background = "#F5F7FA",
      Surface = "#FFFFFF",
      DrawerBackground = "#E8EEF3",
      DrawerText = "#123047",
      AppbarBackground = "#123047",
      AppbarText = "#FFFFFF",
    },
    LayoutProperties = new LayoutProperties
    {
      DefaultBorderRadius = "6px",
      DrawerWidthLeft = "260px",
    },
    Typography = new Typography
    {
      Default = new DefaultTypography { FontFamily = ["system-ui", "sans-serif"], FontSize = "0.95rem" },
      H1 = new H1Typography { FontSize = "2rem", FontWeight = "700" },
      H2 = new H2Typography { FontSize = "1.4rem", FontWeight = "700" },
      H3 = new H3Typography { FontSize = "1.15rem", FontWeight = "700" },
      Button = new ButtonTypography { TextTransform = "none", FontWeight = "700" },
    },
  };
}
