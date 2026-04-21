const { Plugin } = require('obsidian');

module.exports = class GdBlockIdsPlugin extends Plugin {

  async onload() {
    // Process all .gd files on startup
    const gdFiles = this.app.vault.getFiles().filter(f => f.extension === 'gd');
    for (const file of gdFiles) {
      await this.injectBlockIds(file);
    }

    // Re-process a .gd file whenever it is modified
    this.registerEvent(
      this.app.vault.on('modify', async (file) => {
        if (file.extension === 'gd') {
          await this.injectBlockIds(file);
        }
      })
    );

    // Also handle newly created .gd files
    this.registerEvent(
      this.app.vault.on('create', async (file) => {
        if (file.extension === 'gd') {
          await this.injectBlockIds(file);
        }
      })
    );
  }

  async injectBlockIds(file) {
    const fc = this.app.metadataCache.fileCache;
    const mc = this.app.metadataCache.metadataCache;

    const fileEntry = fc[file.path];
    if (!fileEntry?.hash) return;

    const hash = fileEntry.hash;
    if (!mc[hash]) return;

    const content = await this.app.vault.read(file);
    const lines = content.split('\n');

    // Track character offsets as we go
    const blockIds = {};
    let insideFence = false;
    let offset = 0;

    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      const trimmed = line.trim();

      // Detect fence open/close (lines starting with ```)
      if (trimmed.startsWith('```')) {
        insideFence = !insideFence;
        offset += line.length + 1; // +1 for the newline
        continue;
      }

      if (insideFence) {
        // Look for # ^block-id at the end of the line
        // Matches: anything + whitespace + # ^identifier (at end of line)
        const match = line.match(/\s#\s\^([\w-]+)\s*$/);
        if (match) {
          const id = match[1].toLowerCase();
          blockIds[id] = {
            id: id,
            position: {
              start: { line: i, col: 0, offset: offset },
              end:   { line: i, col: line.length, offset: offset + line.length }
            }
          };
        }
      }

      offset += line.length + 1;
    }

    // Merge our found ids into the existing blocks for this file,
    // being careful not to wipe out any ^ids the CFE plugin already found
    // outside fences.
    if (Object.keys(blockIds).length > 0) {
      mc[hash].blocks = Object.assign({}, mc[hash].blocks ?? {}, blockIds);
    }
  }
};