import { constants } from "node:fs";
import { link, lstat, mkdtemp, open, realpath, rm, writeFile } from "node:fs/promises";
import { isAbsolute, join } from "node:path";
import { createHash } from "node:crypto";

export const fileLimit = 25 * 1024 * 1024;
export const shaPattern = "^[0-9a-f]{64}$";
export const namePattern = "^[A-Za-z0-9](?:[A-Za-z0-9._ -]{0,125}[A-Za-z0-9_-])?$";
export const sha256 = bytes => createHash("sha256").update(bytes).digest("hex");

export class FileError extends Error {
  constructor(code, message) {
    super(message);
    this.code = code;
  }
}

export function fileExchange(configured) {
  async function root() {
    if (!configured || !isAbsolute(configured)) {
      throw new FileError("mcp.file-directory", "Configure an absolute private MCP file directory.");
    }
    try {
      const directory = await lstat(configured);
      if (!directory.isDirectory() || (directory.mode & 0o077) !== 0 ||
          directory.uid !== process.getuid?.() || await realpath(configured) !== configured) {
        throw new Error("Not a private canonical directory.");
      }
      return configured;
    } catch {
      throw new FileError("mcp.file-directory", "The MCP file directory must be private, owned by you, and contain no symbolic links in its path.");
    }
  }

  function nameOf(name) {
    if (typeof name !== "string" || !new RegExp(namePattern).test(name) || name.includes("..")) {
      throw new FileError("mcp.file-name", "Use one ordinary file name within the MCP file directory.");
    }
    return name;
  }

  return {
    async source(name, expectedSha256, limit) {
      const path = join(await root(), nameOf(name));
      if (!constants.O_NOFOLLOW || !constants.O_NONBLOCK) {
        throw new FileError("mcp.file-platform", "The local platform cannot safely open this file.");
      }
      let file;
      try {
        file = await open(path, constants.O_RDONLY | constants.O_NOFOLLOW | constants.O_NONBLOCK);
        const item = await file.stat();
        if (!item.isFile() || item.nlink !== 1 || item.uid !== process.getuid?.()) {
          throw new FileError("mcp.file-type", "Choose a regular file that you own.");
        }
        if (item.size <= 0 || item.size > limit) {
          throw new FileError("mcp.file-size", "The selected file is empty or exceeds the permitted size.");
        }
        const buffer = Buffer.alloc(item.size + 1);
        let offset = 0;
        while (offset < buffer.length) {
          const { bytesRead } = await file.read(buffer, offset, buffer.length - offset, offset);
          if (bytesRead === 0) break;
          offset += bytesRead;
        }
        const content = buffer.subarray(0, offset);
        if (offset !== item.size || sha256(content) !== expectedSha256) {
          throw new FileError("mcp.file-changed", "The bytes do not match the selected file hash.");
        }
        return content;
      } catch (error) {
        if (error instanceof FileError) throw error;
        throw new FileError("mcp.file-unavailable", "The selected local file could not be opened safely.");
      } finally {
        await file?.close();
      }
    },

    async destination(name) {
      const directory = await root();
      const path = join(directory, nameOf(name));
      try {
        await lstat(path);
        throw new FileError("mcp.file-exists", "Choose a new destination file name.");
      } catch (error) {
        if (error.code !== "ENOENT") throw error;
      }
      return {
        async save(content) {
          // Link the completed private file into place without replacing an existing
          // file or exposing partial bytes on a failed download.
          const temporary = await mkdtemp(join(directory, ".mcp-download-"));
          const privateFile = join(temporary, "download");
          try {
            await writeFile(privateFile, content, { mode: 0o600, flag: "wx" });
            await link(privateFile, path);
          } catch (error) {
            if (error.code === "EEXIST") throw new FileError("mcp.file-exists", "Choose a new destination file name.");
            throw new FileError("mcp.file-save", "The download could not be saved to the private directory.");
          } finally {
            await rm(temporary, { recursive: true, force: true });
          }
        }
      };
    }
  };
}
