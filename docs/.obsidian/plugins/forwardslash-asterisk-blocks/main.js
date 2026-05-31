'use strict';

const { Plugin } = require('obsidian');
const { Decoration, EditorView } = require('@codemirror/view');
const { StateField, RangeSetBuilder } = require('@codemirror/state');

// ── Block collector ────────────────────────────────────────────────────────────

// Find all /*...*/ spans in the raw text.
function collectBlocks(text) {
  const blocks = [];
  const OPEN  = '/*';
  const CLOSE = '*/';
  let searchFrom = 0;
  while (true) {
    const openIdx = text.indexOf(OPEN, searchFrom);
    if (openIdx === -1) break;
    const closeIdx = text.indexOf(CLOSE, openIdx + 2);
    if (closeIdx === -1) break;
    const closeEnd = closeIdx + 2;
    blocks.push({ from: openIdx, to: closeEnd, openIdx, closeIdx });
    searchFrom = closeEnd;
  }
  return blocks;
}

// ── Decoration emitter ─────────────────────────────────────────────────────────

function emitBlock(builder, doc, text, block) {
  const { openIdx, closeIdx } = block;
  // Content sits between the 2-char open and 2-char close delimiters.
  const rawContent  = text.slice(openIdx + 2, closeIdx);
  const isMultiLine = rawContent.includes('\n');

  if (isMultiLine) {
    const openLine  = doc.lineAt(openIdx);
    const closeLine = doc.lineAt(closeIdx);

    // Point decoration (from==to) must come before any range decoration at
    // the same position — that's the order we add them in throughout.
    builder.add(openLine.from, openLine.from, Decoration.line({ class: 'block-comment-open' }));
    builder.add(openIdx, openIdx + 2, Decoration.replace({}));

    let pos = openLine.to + 1;
    while (pos <= closeLine.from) {
      const line = doc.lineAt(pos);
      if (line.number < closeLine.number) {
        builder.add(line.from, line.from, Decoration.line({ class: 'block-comment-content' }));
      }
      pos = line.to + 1;
    }

    builder.add(closeLine.from, closeLine.from, Decoration.line({ class: 'block-comment-close' }));
    builder.add(closeIdx, closeIdx + 2, Decoration.replace({}));
  } else {
    builder.add(openIdx, openIdx + 2, Decoration.replace({}));
    if (closeIdx > openIdx + 2) {
      builder.add(openIdx + 2, closeIdx, Decoration.mark({ class: 'block-comment-inline' }));
    }
    builder.add(closeIdx, closeIdx + 2, Decoration.replace({}));
  }
}

// ── Main builder ───────────────────────────────────────────────────────────────

function buildDecorations(state) {
  const builder = new RangeSetBuilder();
  const doc    = state.doc;
  const text   = doc.toString();
  const cursor = state.selection.main.head;

  const blocks = collectBlocks(text).sort((a, b) => a.from - b.from);

  for (const block of blocks) {
    // If cursor is inside this block, skip all decorations so the raw syntax
    // is visible and editable (mirrors Obsidian's bold/italic behaviour).
    if (cursor > block.from && cursor < block.to) continue;

    emitBlock(builder, doc, text, block);
  }

  return builder.finish();
}

// ── StateField (Decoration.line requires StateField, not ViewPlugin) ───────────
const decorationField = StateField.define({
  create(state)     { return buildDecorations(state); },
  update(deco, tr)  {
    if (tr.docChanged || tr.selection) return buildDecorations(tr.state);
    return deco.map(tr.changes);
  },
  provide(field)    { return EditorView.decorations.from(field); },
});

// ── Plugin entry point ─────────────────────────────────────────────────────────
class BlockCommentPlugin extends Plugin {
  async onload()  { this.registerEditorExtension(decorationField); }
  onunload()      {}
}

module.exports = BlockCommentPlugin;