using MudBlazor;

namespace AuditSphereOps.Web.Components.Theme;

/// <summary>
/// Central AuditSphere MudBlazor theme (presentation layer only).
/// Enterprise presentation tokens shared by staff, client and public layouts.
/// </summary>
public static class AuditSphereTheme
{
  public static MudTheme Create() => new()
  {
    PaletteLight = new PaletteLight
    {
      Primary = "#2B6CB0",
      Secondary = "#0B6B65",
      Tertiary = "#0F172A",
      Info = "#2B6CB0",
      Success = "#15803D",
      Warning = "#B45309",
      Error = "#B91C1C",
      Dark = "#0F172A",
      TextPrimary = "#17212B",
      TextSecondary = "#526176",
      Background = "#F8FAFC",
      Surface = "#FFFFFF",
      DrawerBackground = "#0F172A",
      DrawerText = "#E2E8F0",
      AppbarBackground = "#FFFFFF",
      AppbarText = "#17212B",
    },
    LayoutProperties = new LayoutProperties
    {
      DefaultBorderRadius = "6px",
      DrawerWidthLeft = "232px",
    },
    Typography = new Typography
    {
      Default = new DefaultTypography { FontFamily = ["system-ui", "sans-serif"], FontSize = "0.875rem" },
      H1 = new H1Typography { FontSize = "1.75rem", FontWeight = "700" },
      H2 = new H2Typography { FontSize = "1.25rem", FontWeight = "700" },
      H3 = new H3Typography { FontSize = "1.15rem", FontWeight = "700" },
      Button = new ButtonTypography { TextTransform = "none", FontWeight = "700" },
    },
  };
}
