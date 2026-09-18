window.auditSpherePbc = {
  readFileMetadata: function (inputId) {
    const input = document.getElementById(inputId);
    const file = input && input.files && input.files[0];
    return file ? { name: file.name, type: file.type || "application/octet-stream", size: file.size } : null;
  },

  uploadChunks: async function (inputId, uploadId, capability) {
    const input = document.getElementById(inputId);
    const file = input && input.files && input.files[0];
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
    }
    return `Staged ${offset} bytes in ${chunkIndex} chunk(s); trusted completion is still required.`;
  }
};
