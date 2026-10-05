// "/" focuses the staff search only when the user is not typing and no dialog is open (UX-029-AC03).
window.auditSearch = {
  clear() {
    const input = document.getElementById("global-search-input");
    if (input) input.value = "";
  }
};
(() => {
  const editable = (element) => element instanceof HTMLElement &&
    (element.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(element.tagName));
  document.addEventListener("keydown", (event) => {
    if (event.key !== "/" || event.ctrlKey || event.metaKey || event.altKey || event.defaultPrevented) return;
    if (editable(document.activeElement) || document.querySelector(".mud-dialog-container, [role='dialog'][aria-modal='true']")) return;
    const input = document.getElementById("global-search-input");
    if (!input) return;
    event.preventDefault();
    // On narrow screens the field is collapsed; open it through the component's own toggle, then focus.
    if (input.offsetParent === null) {
      input.closest(".audit-search")?.querySelector(".audit-search-toggle")?.click();
      setTimeout(() => input.focus(), 50);
      return;
    }
    input.focus();
  });
})();
