const selectedFiles = new Map();

document.addEventListener("change", event => {
  const input = event.target;
  if (input?.type !== "file" || !input.id.startsWith("pbc-file-")) return;
  const file = input.files?.[0];
  if (file) selectedFiles.set(input.id, file);
  else selectedFiles.delete(input.id);
}, true);

function selectedFile(inputId) {
  const input = document.getElementById(inputId);
  const file = input?.files?.[0];
  if (file) selectedFiles.set(inputId, file);
  return file || selectedFiles.get(inputId) || null;
}

window.auditSpherePbc = {
  readFileMetadata: function (inputId) {
    const file = selectedFile(inputId);
    return file ? { name: file.name, type: file.type || "application/octet-stream", size: file.size } : null;
  },

  /** Binds drag-and-drop and file selection to the component; idempotent per rendered element. */
  bindDropZone: function (zoneId, inputId, dotnetRef) {
    const zone = document.getElementById(zoneId);
    const input = document.getElementById(inputId);
    if (!zone || !input || zone.dataset.bound === "true") return;
    zone.dataset.bound = "true";
    const chosen = file => {
      if (!file) return;
      selectedFiles.set(inputId, file);
      dotnetRef.invokeMethodAsync("OnFileChosen");
    };
    ["dragenter", "dragover"].forEach(name => zone.addEventListener(name, event => {
      event.preventDefault();
      zone.classList.add("drop-zone-active");
    }));
    ["dragleave", "drop"].forEach(name => zone.addEventListener(name, () => zone.classList.remove("drop-zone-active")));
    zone.addEventListener("drop", event => {
      event.preventDefault();
      const file = event.dataTransfer?.files?.[0];
      if (!file) return;
      try { input.files = event.dataTransfer.files; } catch { /* older browsers keep the map entry only */ }
      chosen(file);
    });
    zone.addEventListener("keydown", event => {
      if (event.key === "Enter" || event.key === " ") { event.preventDefault(); input.click(); }
    });
    input.addEventListener("change", () => chosen(input.files?.[0]));
  },

  /** Name, type, size and the SHA-256 of the exact selected bytes, calculated in the browser. */
  describeFile: async function (inputId) {
    const file = selectedFile(inputId);
    if (!file) return null;
    const digest = await crypto.subtle.digest("SHA-256", await file.arrayBuffer());
    const sha256 = Array.from(new Uint8Array(digest), b => b.toString(16).padStart(2, "0")).join("");
    return { name: file.name, type: file.type || "application/octet-stream", size: file.size, sha256 };
  },

  uploadChunks: async function (inputId, uploadId, capability, progressRef) {
    const file = selectedFile(inputId);
    if (!file) throw new Error("Select a file before starting the upload.");

    const chunkSize = 8 * 1024 * 1024;
    let offset = 0;
    let chunkIndex = 0;
    while (offset < file.size) {
      const bytes = await file.slice(offset, Math.min(offset + chunkSize, file.size)).arrayBuffer();
      const digest = await crypto.subtle.digest("SHA-256", bytes);
      const hash = Array.from(new Uint8Array(digest), b => b.toString(16).padStart(2, "0")).join("");
      const response = await fetch(`/api/pbc/uploads/${encodeURIComponent(uploadId)}/chunks/${chunkIndex}`, {
        method: "POST",
        credentials: "same-origin",
        headers: {
          "Content-Type": "application/octet-stream",
          "X-Upload-Offset": String(offset),
          "X-Content-SHA256": hash,
          "X-Pbc-Upload-Capability": capability
        },
        body: bytes
      });
      if (!response.ok) {
        const detail = await response.text();
        throw new Error(`Chunk ${chunkIndex} was rejected (${response.status}): ${detail}`);
      }
      offset += bytes.byteLength;
      chunkIndex += 1;
      if (progressRef) await progressRef.invokeMethodAsync("OnUploadProgress", offset, file.size);
    }
    selectedFiles.delete(inputId);
    return `Staged ${offset} bytes in ${chunkIndex} chunk(s); trusted completion is still required.`;
  },

  clearFile: inputId => selectedFiles.delete(inputId)
};
