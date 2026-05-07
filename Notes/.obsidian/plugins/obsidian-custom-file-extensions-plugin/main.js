/*
THIS IS A COMBINED PLUGIN: Custom File Extensions + Block Comment Decorator
- All original Custom File Extensions functionality is preserved unchanged.
- Block comment decoration (configurable OPEN/CLOSE delimiters) is added on top.
- Delimiter settings are universal across all file extensions.
*/

'use strict';

var __defProp = Object.defineProperty;
var __getOwnPropDesc = Object.getOwnPropertyDescriptor;
var __getOwnPropNames = Object.getOwnPropertyNames;
var __hasOwnProp = Object.prototype.hasOwnProperty;
var __export = (target, all) => {
  for (var name in all)
    __defProp(target, name, { get: all[name], enumerable: true });
};
var __copyProps = (to, from, except, desc) => {
  if (from && typeof from === "object" || typeof from === "function") {
    for (let key of __getOwnPropNames(from))
      if (!__hasOwnProp.call(to, key) && key !== except)
        __defProp(to, key, { get: () => from[key], enumerable: !(desc = __getOwnPropDesc(from, key)) || desc.enumerable });
  }
  return to;
};
var __toCommonJS = (mod) => __copyProps(__defProp({}, "__esModule", { value: true }), mod);

// ── Imports ───────────────────────────────────────────────────────────────────
var main_exports = {};
__export(main_exports, {
  CustomFileExtensions: () => CustomFileExtensions,
  default: () => main_default
});
module.exports = __toCommonJS(main_exports);

const { Plugin, PluginSettingTab, Setting, TextAreaComponent, TextComponent, ButtonComponent, Modal, Platform } = require('obsidian');
const { Decoration, EditorView } = require('@codemirror/view');
const { StateField, RangeSetBuilder } = require('@codemirror/state');

// ═════════════════════════════════════════════════════════════════════════════
// SECTION 1 — BLOCK COMMENT DECORATION ENGINE
// ═════════════════════════════════════════════════════════════════════════════

// Escape a string for use in a RegExp
function escapeRegExp(str) {
  return str.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

// Find all OPEN...CLOSE spans in the raw text.
// Returns array of { from, to, openIdx, closeIdx }
function collectBlocks(text, open, close) {
  const blocks = [];
  const openLen  = open.length;
  const closeLen = close.length;
  let searchFrom = 0;
  while (true) {
    const openIdx = text.indexOf(open, searchFrom);
    if (openIdx === -1) break;
    const closeIdx = text.indexOf(close, openIdx + openLen);
    if (closeIdx === -1) break;
    const closeEnd = closeIdx + closeLen;
    blocks.push({ from: openIdx, to: closeEnd, openIdx, closeIdx });
    searchFrom = closeEnd;
  }
  return blocks;
}

function emitBlock(builder, doc, text, block, openLen, closeLen) {
  const { openIdx, closeIdx } = block;
  const rawContent  = text.slice(openIdx + openLen, closeIdx);
  const isMultiLine = rawContent.includes('\n');

  if (isMultiLine) {
    const openLine  = doc.lineAt(openIdx);
    const closeLine = doc.lineAt(closeIdx);

    builder.add(openLine.from, openLine.from, Decoration.line({ class: 'block-comment-open' }));
    builder.add(openIdx, openIdx + openLen, Decoration.replace({}));

    let pos = openLine.to + 1;
    while (pos <= closeLine.from) {
      const line = doc.lineAt(pos);
      if (line.number < closeLine.number) {
        builder.add(line.from, line.from, Decoration.line({ class: 'block-comment-content' }));
      }
      pos = line.to + 1;
    }

    builder.add(closeLine.from, closeLine.from, Decoration.line({ class: 'block-comment-close' }));
    builder.add(closeIdx, closeIdx + closeLen, Decoration.replace({}));
  } else {
    builder.add(openIdx, openIdx + openLen, Decoration.replace({}));
    if (closeIdx > openIdx + openLen) {
      builder.add(openIdx + openLen, closeIdx, Decoration.mark({ class: 'block-comment-inline' }));
    }
    builder.add(closeIdx, closeIdx + closeLen, Decoration.replace({}));
  }
}

function buildDecorations(state, open, close) {
  // Don't decorate if delimiters are empty or identical (would match everything)
  if (!open || !close || open === close) return Decoration.none;

  const builder = new RangeSetBuilder();
  const doc    = state.doc;
  const text   = doc.toString();
  const cursor = state.selection.main.head;
  const openLen  = open.length;
  const closeLen = close.length;

  const blocks = collectBlocks(text, open, close).sort((a, b) => a.from - b.from);

  for (const block of blocks) {
    // If cursor is inside this block, skip decorations so raw syntax is editable
    if (cursor > block.from && cursor < block.to) continue;
    emitBlock(builder, doc, text, block, openLen, closeLen);
  }

  return builder.finish();
}

// Factory: creates a StateField wired to the plugin's live settings.
// We recreate the field whenever delimiters change (plugin handles reconfiguration).
function makeDecorationField(getOpen, getClose) {
  return StateField.define({
    create(state)    { return buildDecorations(state, getOpen(), getClose()); },
    update(deco, tr) {
      if (tr.docChanged || tr.selection) return buildDecorations(tr.state, getOpen(), getClose());
      return deco.map(tr.changes);
    },
    provide(field)   { return EditorView.decorations.from(field); },
  });
}

// ═════════════════════════════════════════════════════════════════════════════
// SECTION 2 — CFE SETTINGS
// ═════════════════════════════════════════════════════════════════════════════

var _DEFAULT_TYPES = {
  "markdown": [
    "",
    "md",
    "txt",
    "js",
    "css",
    "ts",
    "jsx",
    "tsx",
    "yaml",
    "yml",
    "sass",
    "scss",
    "tex",
    "json",
    "html"
  ]
};

var DEFAULT_SETTINGS = {
  // CFE settings
  types: _DEFAULT_TYPES,
  configIsValid: true,
  errors: {},
  allowMdOverride: false,
  mobileSettings: {
    enabled: false,
    configIsValid: true,
    types: _DEFAULT_TYPES
  },
  // Block comment settings
  blockCommentOpen:  '/*',
  blockCommentClose: '*/',
};

// ═════════════════════════════════════════════════════════════════════════════
// SECTION 3 — SETTINGS TAB
// ═════════════════════════════════════════════════════════════════════════════

var CustomFileExtensionsSettingTab = class extends PluginSettingTab {
  constructor(app, plugin) {
    super(app, plugin);
    this._defaults = void 0;
    this.plugin = plugin;
  }

  display() {
    const { containerEl } = this;
    const example = JSON.stringify(DEFAULT_SETTINGS.types, null, 2);
    containerEl.empty();

    // ── Block Comment Delimiter Section ──────────────────────────────────────
    containerEl.createEl("h2", { text: "Block Comment Delimiters" });
    containerEl.createEl("p", {
      text: "Set the OPEN and CLOSE characters that wrap your block comments. These apply to all file types. Leave both as /* and */ to use the defaults.",
    }).style.color = "var(--text-muted)";

    new Setting(containerEl)
      .setName("Open delimiter")
      .setDesc("Characters that mark the start of a block comment (e.g. /*).")
      .addText(text => {
        text
          .setPlaceholder("/*")
          .setValue(this.plugin.settings.blockCommentOpen)
          .onChange(async (value) => {
            await this.plugin.updateSettings({
              ...this.plugin.settings,
              blockCommentOpen: value,
            });
          });
        text.inputEl.style.fontFamily = "var(--font-monospace)";
        text.inputEl.style.width = "120px";
      });

    new Setting(containerEl)
      .setName("Close delimiter")
      .setDesc("Characters that mark the end of a block comment (e.g. */).")
      .addText(text => {
        text
          .setPlaceholder("*/")
          .setValue(this.plugin.settings.blockCommentClose)
          .onChange(async (value) => {
            await this.plugin.updateSettings({
              ...this.plugin.settings,
              blockCommentClose: value,
            });
          });
        text.inputEl.style.fontFamily = "var(--font-monospace)";
        text.inputEl.style.width = "120px";
      });

    // ── CFE Section ───────────────────────────────────────────────────────────
    containerEl.createEl("h2", { text: "Custom File Extensions Settings" });

    this._config = new Setting(containerEl)
      .setName("Config")
      .setDesc("Valid entry is a JSON object who's property keys are view types and values are arrays of the file types to assign to that view.");

    let exampleText = document.createElement("div");
    exampleText.style.fontSize = "80%";
    exampleText.style.margin = "10px";
    exampleText.innerHTML = `<b>Ex</b>: <code>${example}</code>`;
    this._config.nameEl.parentElement.appendChild(exampleText);

    let configTextArea = new TextAreaComponent(containerEl)
      .setPlaceholder(example)
      .setValue(JSON.stringify(this.plugin.settings.types, null, 2))
      .onChange(async (value) => {
        let parsed = null;
        let next = { ...this.plugin.settings };
        try {
          parsed = JSON.parse(value);
          next.configIsValid = true;
          next.types = parsed;
        } catch (e) {
          next.configIsValid = false;
        }
        this._updateConfigValidity(configTextArea, this.plugin.settings.configIsValid, next.configIsValid);
        await this.plugin.updateSettings(next);
        this._updateErrors();
        this._updateList();
        this._updateProfile();
      });
    configTextArea.inputEl.style.width = "100%";
    configTextArea.inputEl.style.height = "150px";
    configTextArea.inputEl.style.minHeight = "100px";

    this._mobileConfig = new Setting(containerEl)
      .setName("Mobile Specific Config")
      .setDesc("If enabled: the config settings below will be used on mobile devices, and the above settings.")
      .addToggle((toggle) => {
        toggle.setValue(this.plugin.settings.mobileSettings.enabled).onChange(async (value) => {
          let next = {
            ...this.plugin.settings,
            mobileSettings: { ...this.plugin.settings.mobileSettings, enabled: value }
          };
          await this.plugin.updateSettings(next);
          this._updateMobileConfigVisible(mobileConfigField, value);
          this._updateErrors();
          this._updateList();
          this._updateProfile();
        });
        return toggle;
      });

    let mobileConfigField = new TextAreaComponent(containerEl)
      .setPlaceholder(example)
      .setValue(this.plugin.settings.mobileSettings.types
        ? JSON.stringify(this.plugin.settings.mobileSettings.types, null, 2)
        : "")
      .onChange(async (value) => {
        let next = {
          ...this.plugin.settings,
          mobileSettings: { ...this.plugin.settings.mobileSettings, types: void 0 }
        };
        if (value !== "" && value !== null && value !== void 0) {
          try {
            let parsed = JSON.parse(value);
            next.mobileSettings.configIsValid = true;
            next.mobileSettings.types = parsed;
          } catch (e) {
            next.mobileSettings.configIsValid = false;
          }
        }
        this._updateConfigValidity(mobileConfigField, this.plugin.settings.mobileSettings.configIsValid, next.mobileSettings.configIsValid);
        await this.plugin.updateSettings(next);
        this._updateErrors();
        this._updateList();
        this._updateProfile();
      });
    mobileConfigField.inputEl.style.width = "100%";
    mobileConfigField.inputEl.style.height = "150px";
    mobileConfigField.inputEl.style.minHeight = "100px";
    this._updateMobileConfigVisible(mobileConfigField, this.plugin.settings.mobileSettings.enabled);

    new Setting(containerEl)
      .setName("Allow Override Of .md Extension")
      .setDesc("If enabled: the .md extension will be allowed to override the default markdown view type. This is disabled by default to prevent unexpected behavior.")
      .addToggle((toggle) => {
        toggle.setValue(this.plugin.settings.allowMdOverride).onChange(async (value) => {
          let next = { ...this.plugin.settings, allowMdOverride: value };
          await this.plugin.updateSettings(next);
        });
        return toggle;
      });

    containerEl.createEl("h3", { text: "Errors" });
    this._errors = containerEl.createEl("p", { text: "None" });
    this._errors.style.whiteSpace = "pre-line";

    containerEl.createEl("h3", { text: "Active View Types and Extensions" });
    this._views = containerEl.createEl("p");
    this._views.style.whiteSpace = "pre-line";

    this._updateErrors();
    this._updateList();
    this._updateProfile();
  }

  _updateMobileConfigVisible(mobileConfigField, mobileSettingsEnabled) {
    mobileConfigField.inputEl.style.display = mobileSettingsEnabled ? "block" : "none";
  }

  _updateConfigValidity(text, prevWasValid, nextIsValid) {
    if (prevWasValid !== nextIsValid) {
      if (prevWasValid) {
        if (!this._defaults) {
          this._defaults = {
            color: text.inputEl.style.color,
            borderColor: text.inputEl.style.borderColor,
            borderWidth: text.inputEl.style.borderWidth
          };
        }
        text.inputEl.style.color = "var(--text-error)";
        text.inputEl.style.borderColor = "var(--background-modifier-error-rgb)";
        text.inputEl.style.borderWidth = "3px";
      } else if (this._defaults) {
        text.inputEl.style.color = this._defaults.color;
        text.inputEl.style.borderColor = this._defaults.borderColor;
        text.inputEl.style.borderWidth = this._defaults.borderWidth;
      }
    }
  }

  _updateErrors() {
    if (Object.keys(this.plugin.settings.errors).length === 0) {
      this._errors.innerHTML = `None`;
      this._errors.style.color = "green";
    } else {
      this._errors.innerHTML = `Errors: <ul>${Object.keys(this.plugin.settings.errors).map((k) => `<li><b>${k}</b>: ${this.plugin.settings.errors[k]}</li>`).join("")}</ul>`;
      this._errors.style.color = "var(--text-error)";
    }
  }

  _updateProfile() {
    if (this.plugin.useMobile) {
      this._mobileConfig.nameEl.innerHTML = `Mobile Specific Config&nbsp;<sup style="color: green">(active)</sup>`;
      this._config.nameEl.innerHTML = `Config&nbsp;<sup style="color: gray">(inactive)</sup>`;
    } else {
      this._config.nameEl.innerHTML = `Config&nbsp;<sup style="color: green">(active)</sup>`;
      this._mobileConfig.nameEl.innerHTML = `Mobile Specific Config&nbsp;<sup style="color: gray">(inactive)</sup>`;
    }
  }

  _updateList() {
    this._views.innerHTML = `<ul>${Object.keys(this.app.viewRegistry.viewByType).sort((a, b) => {
      let extCountForViewKeyA = this._getExtensionsForView(a).length;
      let extCountForViewKeyB = this._getExtensionsForView(b).length;
      return extCountForViewKeyB - extCountForViewKeyA;
    }).map((viewType) => {
      const extensions = this._getExtensionsForView(viewType);
      return `<li>${extensions.length > 0
        ? `<b ${_copy()}>${viewType}</b>`
        : `<span ${_copy()} style="color: gray">${viewType}</span>`
      }${extensions.length
        ? `: ${extensions.sort((a, b) => b.length - a.length).map((ext) => ext
            ? `<code ${_copy()}>${ext}</code>`
            : `<code>""</code> <span style="color: gray"><i>(extensionless)</i></span>`
          ).join(", ")}`
        : ``
      }</li>`;
    }).join("")}</ul>`;

    function _copy() {
      return `
      onmouseover="this.style.textDecoration='underline';"
      onmouseout="this.style.textDecoration='none';"
      title="Click to copy"
      onclick="
        navigator.clipboard.writeText(this.innerText);
        new Notification('Custom File Extensions Plugin', {body:'Copied: \\'' + this.innerText + '\\', to clipboard.'});
      "`;
    }
  }

  _getExtensionsForView(view) {
    return Object.entries(this.app.viewRegistry.typeByExtension)
      .filter(([, v]) => v === view)
      .map(([ext, _]) => ext);
  }
};

// ═════════════════════════════════════════════════════════════════════════════
// SECTION 4 — EDIT EXTENSION MODAL (unchanged from CFE)
// ═════════════════════════════════════════════════════════════════════════════

var EditExtensionModal = class extends Modal {
  constructor(plugin, target) {
    var _a;
    super(plugin.app);
    this.plugin = plugin;
    this.target = target;
    (_a = this.target) != null ? _a : this.target = this.plugin.app.vault.getRoot();
    this._path = this.target.path.split("/").slice(0, -1).join("/");
    let lastPart = this.target.path.split("/").last();
    this._name = lastPart.split(".")[0];
    let lastParts = lastPart == null ? void 0 : lastPart.split(".");
    this._originalExtension = this._newExtension = lastParts.length == 1 ? "" : lastParts.last();
  }

  onOpen() {
    const { contentEl } = this;
    contentEl.style.display = "flex";
    contentEl.style.flexDirection = "column";
    contentEl.style.alignItems = "center";
    const fileNameDisplay = contentEl.createEl("span");
    fileNameDisplay.style.flexGrow = "1";
    fileNameDisplay.style.marginRight = "10px";
    fileNameDisplay.style.fontWeight = "bold";
    fileNameDisplay.style.textAlign = "center";
    fileNameDisplay.innerHTML = this._buildFullPath();
    const formDiv = contentEl.createEl("div");
    formDiv.style.display = "flex";
    formDiv.style.alignItems = "center";
    const fileNameInput = new TextComponent(formDiv);
    fileNameInput.inputEl.style.flexGrow = "1";
    fileNameInput.inputEl.style.marginRight = "10px";
    fileNameInput.setValue(this._originalExtension);
    fileNameInput.inputEl.addEventListener("keypress", (e) => {
      if (e.key === "Enter") { this._submit(); }
      else if (e.key === "Escape") { this.close(); }
    });
    fileNameInput.onChange((value) => {
      this._newExtension = value.startsWith(".") ? value.slice(1) : value;
      fileNameDisplay.innerHTML = this._buildFullPath();
    });
    const submitButton = new ButtonComponent(formDiv);
    submitButton.setCta();
    submitButton.setButtonText("Rename");
    submitButton.onClick(() => this._submit());
  }

  onClose() {
    const { contentEl } = this;
    contentEl.empty();
  }

  async _submit() {
    this.close();
    let newPath = this._buildFullPath();
    await this.app.vault.rename(this.target, newPath);
  }

  _buildFullPath() {
    return this._path + "/" + this._name + (!!this._newExtension ? "." : "") + this._newExtension;
  }
};

// ═════════════════════════════════════════════════════════════════════════════
// SECTION 5 — MAIN PLUGIN CLASS
// ═════════════════════════════════════════════════════════════════════════════

var CustomFileExtensions = class extends Plugin {
  get settings() { return this._settings; }
  get useMobile() { return Platform.isMobile && this.settings.mobileSettings.enabled; }

  async onload() {
    super.onload();
    await this.loadSettings();

    // ── Register block comment decoration ─────────────────────────────────────
    // We hold a reference to the current field so we can swap it when settings change.
    this._decorationField = makeDecorationField(
      () => this._settings.blockCommentOpen,
      () => this._settings.blockCommentClose,
    );
    this.registerEditorExtension(this._decorationField);

    // ── CFE setup (unchanged) ─────────────────────────────────────────────────
    if (this._settings.allowMdOverride) {
      this.app.viewRegistry.unregisterExtensions(["md"]);
    }
    this.registerEvent(this._buildFileContextMenuEditExtensionItem());
    this.addSettingTab(new CustomFileExtensionsSettingTab(this.app, this));
    this._patchMetadataCache();
    this._apply();
    this._reindexCustomFiles();

    setTimeout(async () => {
      const customExts = this._getCustomMarkdownExts();
      for (const file of this.app.vault.getFiles()) {
        if (customExts.has(file.extension)) {
          await this._injectFencedBlockIds(file);
        }
      }
    }, 3000);

    this._fencedBlockIdEventRef = this.app.metadataCache.on('changed', async (file) => {
      if (this._getCustomMarkdownExts().has(file.extension)) {
        await this._injectFencedBlockIds(file);
      }
    });
  }

  onunload() {
    this._unpatchMetadataCache();
    this._unapply(this.settings);
    if (this._fencedBlockIdEventRef) {
      this.app.metadataCache.offref(this._fencedBlockIdEventRef);
    }
    try {
      this.registerExtensions([".md"], "markdown");
    } catch (e) {}
  }

  // ── Settings ───────────────────────────────────────────────────────────────

  async loadSettings() {
    this._settings = Object.assign({}, DEFAULT_SETTINGS, await this.loadData());
    // Ensure block comment keys exist when loading old data.json that pre-dates this combined plugin
    if (this._settings.blockCommentOpen  == null) this._settings.blockCommentOpen  = '/*';
    if (this._settings.blockCommentClose == null) this._settings.blockCommentClose = '*/';
  }

  async resetSettings() {
    this._unapply(this.settings);
    await this.updateSettings(DEFAULT_SETTINGS);
    this._apply();
  }

  async updateSettings(newSettings) {
    this._unapply(newSettings);
    this._settings = newSettings;
    await this.saveData(this.settings);
    this._apply();
    // Force all open editors to re-render decorations with the new delimiters.
    this.app.workspace.updateOptions();
  }

  // ── CFE apply/unapply (unchanged) ─────────────────────────────────────────

  _apply() {
    var _a;
    if (this.useMobile) {
      this._applyConfig((_a = this.settings.mobileSettings.types) != null ? _a : this.settings.types);
    } else {
      this._applyConfig(this.settings.types);
    }
  }

  _tryToApply(fileType, view) {
    if (!this.settings.allowMdOverride && fileType === "md") return;
    try {
      this.registerExtensions([fileType], view);
    } catch (e) {
      let current = this.app.viewRegistry.getTypeByExtension(fileType);
      let message;
      if (current) {
        message = `${fileType} is already registered to ${current}.`;
      } else {
        message = `${e}`;
      }
      message = `Could not register extension: '${fileType}' to view type: ${view}. ${message}`;
      new Notification("Error: Custom File Extensions Plugin", { body: message });
      console.error(message);
      this._settings.errors[fileType] = message;
      return;
    }
    if (view === "markdown" && fileType !== "md") {
      try {
        const mdHandler = this.app.embedRegistry.embedByExtension["md"];
        if (mdHandler) {
          this.app.embedRegistry.embedByExtension[fileType] = mdHandler;
        }
      } catch (e) {
        console.warn(`[CustomFileExtensions] Could not register embed handler for .${fileType}:`, e);
      }
    }
  }

  _applyConfig(extensionsByViewType) {
    this._settings.errors = {};
    for (const view in extensionsByViewType) {
      for (const fileType of extensionsByViewType[view]) {
        this._tryToApply(fileType.toLowerCase(), view);
      }
    }
  }

  _unapply(newSettings) {
    var _a;
    if (this.useMobile) {
      this._unapplyConfig((_a = this.settings.mobileSettings.types) != null ? _a : this.settings.types, newSettings.allowMdOverride);
    } else {
      this._unapplyConfig(this.settings.types, newSettings.allowMdOverride);
    }
  }

  _unapplyConfig(extensionsByViewType, allowMdOverride) {
    for (const [view, extensions] of Object.entries(extensionsByViewType)) {
      for (const extension of extensions) {
        if (allowMdOverride || extension !== "md") {
          if (!this._settings.errors[extension]) {
            try {
              this.app.viewRegistry.unregisterExtensions([extension]);
            } catch (e) {
              const message = `Could not unregister extension: '${extension}'`;
              new Notification("Error: Custom File Extensions Plugin", { body: message });
              console.error(message);
            }
            if (view === "markdown" && extension !== "md") {
              try {
                delete this.app.embedRegistry.embedByExtension[extension];
              } catch (e) {}
            }
          }
        }
      }
    }
  }

  // ── CFE metadata cache patching (unchanged) ────────────────────────────────

  _patchMetadataCache() {
    const proto = Object.getPrototypeOf(this.app.metadataCache);
    if (proto._cfe_patched) return;
    const viewRegistry = this.app.viewRegistry;

    const originalCompute = proto.computeFileMetadataAsync;
    proto._cfe_originalCompute = originalCompute;
    proto.computeFileMetadataAsync = function(file) {
      if (file && file.extension !== "md" && viewRegistry.getTypeByExtension(file.extension) === "markdown") {
        const realExt = file.extension;
        file.extension = "md";
        try { return originalCompute.call(this, file); }
        finally { file.extension = realExt; }
      }
      return originalCompute.call(this, file);
    };

    const originalGetCache = proto.getCache;
    proto._cfe_originalGetCache = originalGetCache;
    proto.getCache = function(path) {
      const ext = path.includes(".") ? path.split(".").pop().toLowerCase() : "";
      if (ext !== "md" && viewRegistry.getTypeByExtension(ext) === "markdown") {
        if (!this.fileCache.hasOwnProperty(path)) return null;
        const hash = this.fileCache[path].hash;
        if (!hash) return null;
        return this.metadataCache[hash] || null;
      }
      return originalGetCache.call(this, path);
    };

    proto._cfe_patched = true;
  }

  _unpatchMetadataCache() {
    const proto = Object.getPrototypeOf(this.app.metadataCache);
    if (!proto._cfe_patched) return;
    proto.computeFileMetadataAsync = proto._cfe_originalCompute;
    proto.getCache = proto._cfe_originalGetCache;
    delete proto._cfe_originalCompute;
    delete proto._cfe_originalGetCache;
    delete proto._cfe_patched;
  }

  // ── CFE reindex + fenced block IDs (unchanged) ────────────────────────────

  _reindexCustomFiles() {
    const customExts = new Set(
      Object.entries(this.app.viewRegistry.typeByExtension)
        .filter(([ext, type]) => type === "markdown" && ext !== "md")
        .map(([ext]) => ext)
    );
    for (const file of this.app.vault.getFiles()) {
      if (customExts.has(file.extension)) {
        this.app.metadataCache.computeFileMetadataAsync(file);
      }
    }
  }

  _getCustomMarkdownExts() {
    return new Set(
      Object.entries(this.app.viewRegistry.typeByExtension)
        .filter(([ext, type]) => type === "markdown" && ext !== "md")
        .map(([ext]) => ext)
    );
  }

  async _injectFencedBlockIds(file) {
    const fc = this.app.metadataCache.fileCache;
    const mc = this.app.metadataCache.metadataCache;

    if (!fc[file.path]?.hash) {
      this.app.metadataCache.computeFileMetadataAsync(file);
      for (let i = 0; i < 12; i++) {
        await new Promise(r => setTimeout(r, 250));
        if (fc[file.path]?.hash) break;
      }
    }

    const fileEntry = fc[file.path];
    if (!fileEntry?.hash) return;
    const hash = fileEntry.hash;
    if (!mc[hash]) return;

    const content = await this.app.vault.read(file);
    const lines = content.split('\n');

    const fencedBlockIds = {};
    let insideFence = false;
    let offset = 0;

    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      if (line.trimStart().startsWith('```')) {
        insideFence = !insideFence;
        offset += line.length + 1;
        continue;
      }
      if (insideFence) {
        const match = line.match(/\s\^([\w-]+)\s*$/);
        if (match) {
          const id = match[1].toLowerCase();
          fencedBlockIds[id] = {
            id: id,
            position: {
              start: { line: i, col: 0,          offset: offset },
              end:   { line: i, col: line.length, offset: offset + line.length }
            }
          };
        }
      }
      offset += line.length + 1;
    }

    if (Object.keys(fencedBlockIds).length > 0) {
      mc[hash].blocks = Object.assign({}, mc[hash].blocks ?? {}, fencedBlockIds);
    }
  }

  _buildFileContextMenuEditExtensionItem() {
    return this.app.workspace.on("file-menu", (menu, file) => {
      menu.addItem((item) => {
        item.setTitle("Edit Extension").setIcon("pencil").onClick(() => new EditExtensionModal(this, file).open());
      });
    });
  }
};

var main_default = CustomFileExtensions;

/* nosourcemap */
