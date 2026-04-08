'use strict';

const { Plugin } = require('obsidian');
const { Decoration, EditorView, WidgetType } = require('@codemirror/view');
const { StateField, RangeSetBuilder } = require('@codemirror/state');

// ── Widget rendered in place of the entire """...""" span ──────────────────────
class TripleQuoteWidget extends WidgetType {
  constructor(content) {
    super();
    this.content = content;
  }

  toDOM() {
    const el = document.createElement('div');
    el.className = 'triple-quote-block';
    el.textContent = this.content;
    return el;
  }

  ignoreEvent() {
    return false;
  }
}

// ── Decoration builder (works on EditorState, not EditorView) ─────────────────
function buildDecorations(state) {
  const builder = new RangeSetBuilder();
  const doc = state.doc;
  const text = doc.toString();
  const cursorPos = state.selection.main.head;

  const OPEN = '"""';
  let searchFrom = 0;

  while (true) {
    const openIdx = text.indexOf(OPEN, searchFrom);
    if (openIdx === -1) break;

    const contentStart = openIdx + 3;
    const closeIdx = text.indexOf(OPEN, contentStart);
    if (closeIdx === -1) break; // unclosed — stop

    const closeEnd = closeIdx + 3;
    const rawContent = text.slice(contentStart, closeIdx);

    // Strip exactly one leading and one trailing newline so that:
    //   """          renders the same as  """text"""
    //   text
    //   """
    const content = rawContent.replace(/^\n/, '').replace(/\n$/, '');

    // If the cursor is inside this match, skip rendering so the user can
    // edit the raw text normally.
    const cursorInside = cursorPos > openIdx && cursorPos < closeEnd;

    if (!cursorInside) {
      const isMultiLine = rawContent.includes('\n');

      if (isMultiLine) {
        // Block decorations must cover full line ranges and require a
        // StateField (not a ViewPlugin) — that's why we're here.
        const startLine = doc.lineAt(openIdx);
        const endLine = doc.lineAt(closeEnd);
        builder.add(
          startLine.from,
          endLine.to,
          Decoration.replace({
            widget: new TripleQuoteWidget(content),
            block: true,
          })
        );
      } else {
        builder.add(
          openIdx,
          closeEnd,
          Decoration.replace({ widget: new TripleQuoteWidget(content) })
        );
      }
    }

    searchFrom = closeEnd;
  }

  return builder.finish();
}

// ── StateField<DecorationSet> — the only CM6 type allowed to hold block decs ──
const tripleQuoteField = StateField.define({
  create(state) {
    return buildDecorations(state);
  },
  update(decorations, tr) {
    // Rebuild whenever the document or selection changes.
    if (tr.docChanged || tr.selection) {
      return buildDecorations(tr.state);
    }
    // Otherwise map existing decoration positions through any changes.
    return decorations.map(tr.changes);
  },
  provide(field) {
    return EditorView.decorations.from(field);
  },
});

// ── Plugin entry point ─────────────────────────────────────────────────────────
class TripleQuoteBlockPlugin extends Plugin {
  async onload() {
    this.registerEditorExtension(tripleQuoteField);
  }

  onunload() {}
}

module.exports = TripleQuoteBlockPlugin;
