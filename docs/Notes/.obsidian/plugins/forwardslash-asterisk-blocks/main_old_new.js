'use strict';

const { Plugin } = require('obsidian');
const { Decoration, EditorView, WidgetType } = require('@codemirror/view');
const { StateField, RangeSetBuilder } = require('@codemirror/state');

// ── Copy button widget ─────────────────────────────────────────────────────────
class CopyButton extends WidgetType {
  constructor(content) {
    super();
    this.content = content;
  }

  toDOM() {
    const btn = document.createElement('button');
    btn.className = 'hash-fence-copy-btn';
    btn.textContent = 'Copy';
    btn.addEventListener('click', (e) => {
      e.preventDefault();
      e.stopPropagation();
      navigator.clipboard.writeText(this.content).then(() => {
        btn.textContent = 'Copied!';
        setTimeout(() => { btn.textContent = 'Copy'; }, 2000);
      });
    });
    return btn;
  }

  ignoreEvent() { return false; }
}

// ── Block collectors ───────────────────────────────────────────────────────────

// Find all """...""" spans in the raw text.
function collectTripleQuoteBlocks(text) {
  const blocks = [];
  const OPEN = '"""';
  let searchFrom = 0;
  while (true) {
    const openIdx = text.indexOf(OPEN, searchFrom);
    if (openIdx === -1) break;
    const closeIdx = text.indexOf(OPEN, openIdx + 3);
    if (closeIdx === -1) break;
    const closeEnd = closeIdx + 3;
    blocks.push({ type: 'triple-quote', from: openIdx, to: closeEnd, openIdx, closeIdx });
    searchFrom = closeEnd;
  }
  return blocks;
}

// Find all  "#``"..."#``"  fences (line-based).
// Opening line: exactly "#``" optionally followed by a language name.
// Closing line: exactly "#``" (with optional trailing whitespace).
const OPEN_FENCE  = /^#``\w*\s*$/;
const CLOSE_FENCE = /^#``\s*$/;

function collectHashFenceBlocks(doc) {
  const blocks = [];
  let openLine = null;
  for (let i = 1; i <= doc.lines; i++) {
    const line = doc.line(i);
    if (!openLine) {
      if (OPEN_FENCE.test(line.text)) openLine = line;
    } else {
      if (CLOSE_FENCE.test(line.text)) {
        blocks.push({ type: 'hash-fence', from: openLine.from, to: line.to, openLine, closeLine: line });
        openLine = null;
      }
    }
  }
  return blocks;
}

// ── Decoration emitters ────────────────────────────────────────────────────────
// Each function receives an already-open RangeSetBuilder and appends to it.
// Caller guarantees decorations are added in document order.

function emitTripleQuote(builder, doc, text, block) {
  const { openIdx, closeIdx } = block;
  const rawContent = text.slice(openIdx + 3, closeIdx);
  const isMultiLine = rawContent.includes('\n');

  if (isMultiLine) {
    const openLine  = doc.lineAt(openIdx);
    const closeLine = doc.lineAt(closeIdx);

    // Point decoration (from==to) must come before any range decoration at
    // the same position — that's the order we add them in throughout.
    builder.add(openLine.from, openLine.from, Decoration.line({ class: 'triple-quote-open' }));
    builder.add(openIdx, openIdx + 3, Decoration.replace({}));

    let pos = openLine.to + 1;
    while (pos <= closeLine.from) {
      const line = doc.lineAt(pos);
      if (line.number < closeLine.number) {
        builder.add(line.from, line.from, Decoration.line({ class: 'triple-quote-content' }));
      }
      pos = line.to + 1;
    }

    builder.add(closeLine.from, closeLine.from, Decoration.line({ class: 'triple-quote-close' }));
    builder.add(closeIdx, closeIdx + 3, Decoration.replace({}));
  } else {
    builder.add(openIdx, openIdx + 3, Decoration.replace({}));
    if (closeIdx > openIdx + 3) {
      builder.add(openIdx + 3, closeIdx, Decoration.mark({ class: 'triple-quote-inline' }));
    }
    builder.add(closeIdx, closeIdx + 3, Decoration.replace({}));
  }
}

function emitHashFence(builder, doc, block) {
  const { openLine, closeLine } = block;

  // Extract the content between the fences for the copy button.
  // openLine.to+1 = first char after opening fence's newline (start of content).
  // closeLine.from = start of closing fence line; subtract 1 to exclude the
  // newline that precedes it, giving us content without a trailing newline.
  const content = doc.sliceString(openLine.to + 1, closeLine.from - 1);

  // Opening fence line: style it, hide the "#``" text, add copy button.
  // Point decorations (from==to) must be added before range decorations at
  // the same position. The widget goes at openLine.to (end of the line,
  // after the collapsed replace range) with side:1 so it renders after it.
  builder.add(openLine.from, openLine.from, Decoration.line({ class: 'hash-fence-open' }));
  builder.add(openLine.from, openLine.to, Decoration.replace({}));
  builder.add(openLine.to, openLine.to,
    Decoration.widget({ widget: new CopyButton(content), side: 1 }));

  // Content lines.
  let pos = openLine.to + 1;
  while (pos <= closeLine.from) {
    const line = doc.lineAt(pos);
    if (line.number < closeLine.number) {
      builder.add(line.from, line.from, Decoration.line({ class: 'hash-fence-content' }));
    }
    pos = line.to + 1;
  }

  // Closing fence line: style it, then hide the "#``" text.
  builder.add(closeLine.from, closeLine.from, Decoration.line({ class: 'hash-fence-close' }));
  builder.add(closeLine.from, closeLine.to, Decoration.replace({}));
}

// ── Main builder ───────────────────────────────────────────────────────────────

function buildDecorations(state) {
  const builder = new RangeSetBuilder();
  const doc    = state.doc;
  const text   = doc.toString();
  const cursor = state.selection.main.head;

  // Collect blocks from both syntaxes, merge in document order.
  const blocks = [
    ...collectTripleQuoteBlocks(text),
    ...collectHashFenceBlocks(doc),
  ].sort((a, b) => a.from - b.from);

  for (const block of blocks) {
    // If cursor is inside this block, skip all decorations so the raw syntax
    // is visible and editable (mirrors Obsidian's bold/italic behaviour).
    if (cursor > block.from && cursor < block.to) continue;

    if (block.type === 'triple-quote') {
      emitTripleQuote(builder, doc, text, block);
    } else {
      emitHashFence(builder, doc, block);
    }
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

// ── Markdown post-processor ────────────────────────────────────────────────────
// Mirrors the CM6 decoration logic for Reading Mode and ![[embed]] previews.
// Called by Obsidian after it converts markdown to HTML, so we walk the
// rendered DOM and apply the same classes the CM6 field applies in the editor.

function processTripleQuotes(el) {
  // Walk all text nodes inside el, find """...""" spans, and replace them
  // with properly classed DOM nodes — mirroring emitTripleQuote().
  //
  // We collect text nodes first because replacing them while walking would
  // invalidate the TreeWalker iterator.
  const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
  const textNodes = [];
  let node;
  while ((node = walker.nextNode())) textNodes.push(node);

  for (const textNode of textNodes) {
    const text = textNode.nodeValue;
    if (!text.includes('"""')) continue;

    const OPEN = '"""';
    const parent = textNode.parentNode;
    const frag = document.createDocumentFragment();
    let searchFrom = 0;
    let lastEnd = 0;
    let found = false;

    while (true) {
      const openIdx = text.indexOf(OPEN, searchFrom);
      if (openIdx === -1) break;
      const closeIdx = text.indexOf(OPEN, openIdx + 3);
      if (closeIdx === -1) break;

      found = true;
      const closeEnd = closeIdx + 3;
      const rawContent = text.slice(openIdx + 3, closeIdx);
      const isMultiLine = rawContent.includes('\n');

      // Text before this block.
      if (openIdx > lastEnd) {
        frag.appendChild(document.createTextNode(text.slice(lastEnd, openIdx)));
      }

      if (isMultiLine) {
        // Wrap the whole block in a div, split content into styled lines.
        const lines = rawContent.split('\n');
        const wrapper = document.createElement('div');
        wrapper.className = 'triple-quote-block';

        const openDiv = document.createElement('div');
        openDiv.className = 'triple-quote-open';
        wrapper.appendChild(openDiv);

        for (const line of lines) {
          const contentDiv = document.createElement('div');
          contentDiv.className = 'triple-quote-content';
          contentDiv.textContent = line;
          wrapper.appendChild(contentDiv);
        }

        const closeDiv = document.createElement('div');
        closeDiv.className = 'triple-quote-close';
        wrapper.appendChild(closeDiv);

        frag.appendChild(wrapper);
      } else {
        // Inline: wrap content in a span with the inline class.
        const span = document.createElement('span');
        span.className = 'triple-quote-inline';
        span.textContent = rawContent;
        frag.appendChild(span);
      }

      lastEnd = closeEnd;
      searchFrom = closeEnd;
    }

    if (!found) continue;

    // Remaining text after the last block.
    if (lastEnd < text.length) {
      frag.appendChild(document.createTextNode(text.slice(lastEnd)));
    }

    parent.replaceChild(frag, textNode);
  }
}

// ── Plugin entry point ─────────────────────────────────────────────────────────
class TripleQuoteBlockPlugin extends Plugin {
  async onload() {
    // CM6 extension: handles Live Preview editor rendering.
    this.registerEditorExtension(decorationField);

    // Post-processor: handles Reading Mode and ![[embed]] previews.
    // MarkdownRenderer.render (used by embeds) produces static HTML and never
    // runs CM6 extensions, so we need this separate path.
    this.registerMarkdownPostProcessor((el) => {
      processTripleQuotes(el);
    });
  }
  onunload() {}
}

module.exports = TripleQuoteBlockPlugin;
