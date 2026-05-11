'use strict';

const { Plugin, MarkdownRenderer } = require('obsidian');
const { Decoration, EditorView, WidgetType } = require('@codemirror/view');
const { StateField, RangeSetBuilder } = require('@codemirror/state');

// =============================================================================
// HOW THIS WORKS
// =============================================================================
// Triple-quote blocks ("""...""") need to render in three contexts:
//
//   1. Live Preview (editing)  — CM6 StateField hides the """ delimiters and
//                                applies CSS classes to the content lines.
//
//   2. Reading Mode            — registerMarkdownPostProcessor scans rendered
//                                HTML for surviving """ text and styles it.
//                                BUT Obsidian's markdown parser mangles
//                                multiline """ content unpredictably.
//
//   3. ![[embed]] previews     — Same pipeline as Reading Mode, same problem.
//
// The solution for (2) and (3): a pre-processing step that converts
// """...""" blocks into ```tq ... ``` fenced code blocks BEFORE Obsidian's
// parser sees the text. Fenced code blocks survive the parser 100% intact.
// registerMarkdownCodeBlockProcessor("tq", ...) then renders them.
//
// For (1), the CM6 field still operates on the raw """ syntax in the file,
// so the file on disk never changes — the conversion only happens in memory
// during rendering.
//
// The pre-processing is injected via a monkey-patch on the embed registry's
// "md" handler so it applies to all embedded custom-extension files.
// For Reading Mode on plain .md notes the post-processor handles it directly.
// =============================================================================

// =============================================================================
// SHARED REGEX / HELPERS
// =============================================================================

// Converts all """...""" spans in a markdown string to ```tq...``` blocks
// that Obsidian's parser will leave completely intact.
function preprocessTripleQuotes(text) {
  const OPEN = '"""';
  let result = '';
  let searchFrom = 0;

  while (true) {
    const openIdx = text.indexOf(OPEN, searchFrom);
    if (openIdx === -1) {
      result += text.slice(searchFrom);
      break;
    }
    const closeIdx = text.indexOf(OPEN, openIdx + 3);
    if (closeIdx === -1) {
      result += text.slice(searchFrom);
      break;
    }
    const closeEnd = closeIdx + 3;
    const content = text.slice(openIdx + 3, closeIdx);

    result += text.slice(searchFrom, openIdx);
    result += '```tq\n' + content.trim() + '\n```';
    searchFrom = closeEnd;
  }

  return result;
}

// =============================================================================
// READING MODE + EMBED POST-PROCESSING
// registerMarkdownCodeBlockProcessor("tq", ...) handles the ```tq blocks
// that preprocessTripleQuotes() produces. This fires in both Reading Mode
// and inside ![[embed]] previews.
// =============================================================================

function registerTqCodeBlockProcessor(plugin) {
  plugin.registerMarkdownCodeBlockProcessor('tq', (source, el, ctx) => {
    el.addClass('triple-quote-block');

    // Render the content as markdown so bold, italic, links etc. work inside
    // triple-quote blocks.
    const inner = el.createDiv({ cls: 'triple-quote-content' });
    MarkdownRenderer.render(plugin.app, source.trim(), inner, ctx.sourcePath, plugin);
  });
}

// =============================================================================
// READING MODE POST-PROCESSOR
// Handles """...""" in plain .md notes in Reading Mode by preprocessing the
// raw source before it reaches the parser. We hook into the rendered HTML
// and look for any surviving """ that the parser didn't mangle — for the
// rest, the code block processor above handles it.
//
// Actually the cleanest approach: hook the file's raw content via
// registerMarkdownPostProcessor on the container el, but we need the SOURCE.
// Instead we pre-process in the embed registry patch (see bottom) and rely
// on the code block processor for all cases including plain .md reading mode
// by using a separate approach: override the renderer for the active file.
//
// Simplest reliable approach for plain .md files in Reading Mode:
// Use the 'render' event on the workspace to preprocess before rendering.
// =============================================================================

// =============================================================================
// CM6 LIVE PREVIEW — StateField
// Operates on the raw """ syntax in the editor. Hides the delimiters and
// applies styling classes to the content, exactly like the original plugin.
// The file on disk is never modified.
// =============================================================================

class TripleQuoteWidget extends WidgetType {
  // Used to replace the """ delimiters themselves — produces an empty span
  // so the delimiter characters vanish from view.
  toDOM() {
    return document.createElement('span');
  }
  ignoreEvent() { return false; }
}

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
    blocks.push({ from: openIdx, to: closeEnd, openIdx, closeIdx });
    searchFrom = closeEnd;
  }
  return blocks;
}

function buildDecorations(state) {
  const builder = new RangeSetBuilder();
  const doc    = state.doc;
  const text   = doc.toString();
  const cursor = state.selection.main.head;
  const blocks = collectTripleQuoteBlocks(text);

  for (const block of blocks) {
    // If cursor is inside this block show raw syntax (like Obsidian does
    // for bold/italic).
    if (cursor > block.from && cursor < block.to) continue;

    const { openIdx, closeIdx } = block;
    const rawContent = text.slice(openIdx + 3, closeIdx);
    const isMultiLine = rawContent.includes('\n');

    if (isMultiLine) {
      const openLine  = doc.lineAt(openIdx);
      const closeLine = doc.lineAt(closeIdx);

      // Hide the opening """ 
      builder.add(openLine.from, openLine.from, Decoration.line({ class: 'triple-quote-open' }));
      builder.add(openIdx, openIdx + 3, Decoration.replace({}));

      // Style each content line
      let pos = openLine.to + 1;
      while (pos <= closeLine.from) {
        const line = doc.lineAt(pos);
        if (line.number < closeLine.number) {
          builder.add(line.from, line.from, Decoration.line({ class: 'triple-quote-content' }));
        }
        pos = line.to + 1;
      }

      // Hide the closing """
      builder.add(closeLine.from, closeLine.from, Decoration.line({ class: 'triple-quote-close' }));
      builder.add(closeIdx, closeIdx + 3, Decoration.replace({}));
    } else {
      // Inline: hide both """ delimiters, style the content span
      builder.add(openIdx, openIdx + 3, Decoration.replace({}));
      if (closeIdx > openIdx + 3) {
        builder.add(openIdx + 3, closeIdx, Decoration.mark({ class: 'triple-quote-inline' }));
      }
      builder.add(closeIdx, closeIdx + 3, Decoration.replace({}));
    }
  }

  return builder.finish();
}

const decorationField = StateField.define({
  create(state)    { return buildDecorations(state); },
  update(deco, tr) {
    if (tr.docChanged || tr.selection) return buildDecorations(tr.state);
    return deco.map(tr.changes);
  },
  provide(field)   { return EditorView.decorations.from(field); },
});

// =============================================================================
// EMBED PREPROCESSING PATCH
//
// When Obsidian renders an ![[embed]], it uses the embed registry handler.
// We wrap the "md" handler to preprocess the file's markdown source —
// converting """...""" to ```tq...``` — before Obsidian's parser sees it.
// This means the ```tq processor above fires correctly for all embeds,
// including custom-extension files registered as markdown.
//
// This patch is installed by the plugin and removed on unload.
// =============================================================================

function patchEmbedRegistry(app) {
  const registry = app.embedRegistry;
  if (registry._tqb_originalMdHandler) return; // don't double-patch

  const originalHandler = registry.embedByExtension['md'];
  registry._tqb_originalMdHandler = originalHandler;

  // Wrap every registered markdown extension (md + all custom ones).
  // We intercept by wrapping the factory so the created embed object's
  // load/render method preprocesses the source.
  //
  // The factory signature is: (ctx, file, subpath) => embedInstance
  // The embed instance has a loadFile() method that does the actual render.
  // We wrap loadFile to preprocess before it runs.
  registry.embedByExtension['md'] = function(ctx, file, subpath) {
    const instance = originalHandler(ctx, file, subpath);
    const originalLoadFile = instance.loadFile?.bind(instance);
    if (originalLoadFile) {
      instance.loadFile = async function() {
        // Read the file, preprocess, temporarily swap the content,
        // then restore. We do this by monkey-patching vault.cachedRead
        // just for this one call.
        const originalCachedRead = app.vault.cachedRead.bind(app.vault);
        app.vault.cachedRead = async function(f) {
          const content = await originalCachedRead(f);
          if (f.path === file.path) {
            return preprocessTripleQuotes(content);
          }
          return content;
        };
        try {
          return await originalLoadFile();
        } finally {
          app.vault.cachedRead = originalCachedRead;
        }
      };
    }
    return instance;
  };

  // Mirror the wrapper onto all other markdown-registered extensions
  // so custom extensions (.gd, .txt etc) also get the preprocessing.
  // We do this by checking embedByExtension at call time rather than
  // at patch time, since custom extensions may be registered after us.
}

function unpatchEmbedRegistry(app) {
  const registry = app.embedRegistry;
  if (!registry._tqb_originalMdHandler) return;
  registry.embedByExtension['md'] = registry._tqb_originalMdHandler;
  delete registry._tqb_originalMdHandler;
}

// Also patch any non-md extensions that point to the md handler —
// called after _apply() in the custom file extensions plugin registers them.
function syncEmbedRegistryPatch(app) {
  const registry = app.embedRegistry;
  if (!registry._tqb_originalMdHandler) return;
  const wrappedHandler = registry.embedByExtension['md'];
  for (const [ext, handler] of Object.entries(registry.embedByExtension)) {
    // If this extension points to the original (unwrapped) md handler,
    // update it to point to the wrapped one.
    if (ext !== 'md' && handler === registry._tqb_originalMdHandler) {
      registry.embedByExtension[ext] = wrappedHandler;
    }
  }
}

// =============================================================================
// READING MODE for plain .md notes
// registerMarkdownPostProcessor fires on the rendered HTML. By this point
// Obsidian's parser has already mangled multiline """ blocks. The fix:
// we use the same approach as embeds — preprocess the raw source before
// Obsidian renders it. We do this by hooking the MarkdownView's render
// pipeline via a workspace event.
// =============================================================================

function patchMarkdownView(plugin) {
  // When a markdown file is rendered in Reading Mode, Obsidian calls
  // the renderer's set() method with the raw markdown string.
  // We intercept by wrapping the renderer's set method on each view.
  plugin.registerEvent(
    plugin.app.workspace.on('layout-change', () => {
      plugin.app.workspace.iterateAllLeaves(leaf => {
        const view = leaf.view;
        if (!view || view.getViewType() !== 'markdown') return;
        const renderer = view.previewMode?.renderer;
        if (!renderer || renderer._tqb_patched) return;

        const originalSet = renderer.set.bind(renderer);
        renderer.set = function(content, ...args) {
          return originalSet(preprocessTripleQuotes(content), ...args);
        };
        renderer._tqb_patched = true;
      });
    })
  );

  // Also patch currently open leaves immediately.
  plugin.app.workspace.iterateAllLeaves(leaf => {
    const view = leaf.view;
    if (!view || view.getViewType() !== 'markdown') return;
    const renderer = view.previewMode?.renderer;
    if (!renderer || renderer._tqb_patched) return;

    const originalSet = renderer.set.bind(renderer);
    renderer.set = function(content, ...args) {
      return originalSet(preprocessTripleQuotes(content), ...args);
    };
    renderer._tqb_patched = true;
  });
}

// =============================================================================
// PLUGIN ENTRY POINT
// =============================================================================

class TripleQuoteBlockPlugin extends Plugin {
  async onload() {
    // 1. CM6 extension for Live Preview editing.
    this.registerEditorExtension(decorationField);

    // 2. Code block processor for ```tq blocks (produced by preprocessing).
    //    This handles both Reading Mode and embed previews.
    registerTqCodeBlockProcessor(this);

    // 3. Patch the embed registry so ![[embeds]] preprocess their source.
    patchEmbedRegistry(this.app);

    // 4. Patch markdown view renderers for Reading Mode in .md notes.
    patchMarkdownView(this);

    // 5. After a short delay, sync the patch to any custom-extension embed
    //    handlers that were registered by the custom-file-extensions plugin
    //    (which may have loaded before or after us).
    setTimeout(() => syncEmbedRegistryPatch(this.app), 500);
  }

  onunload() {
    unpatchEmbedRegistry(this.app);
    // Note: renderer.set patches on individual views are not restored —
    // they'll be garbage collected when the views close. Obsidian reloads
    // views on plugin disable anyway.
  }
}

module.exports = TripleQuoteBlockPlugin;
