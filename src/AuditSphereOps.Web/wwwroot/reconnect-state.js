// Retry/reload control for the custom Blazor reconnection panel in App.razor. No business state is kept here.
(() => {
  document.addEventListener("click", async (event) => {
    const button = event.target instanceof Element ? event.target.closest("[data-reconnect-action]") : null;
    if (!button) return;
    const modal = document.getElementById("components-reconnect-modal");
    if (modal?.classList.contains("components-reconnect-rejected")) { location.reload(); return; }
    try {
      const resumed = await (window.Blazor?.reconnect?.() ?? Promise.resolve(false));
      if (!resumed) location.reload();
    } catch {
      location.reload();
    }
  });
})();
