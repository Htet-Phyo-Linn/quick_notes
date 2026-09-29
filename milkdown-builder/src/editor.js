import { Crepe } from '@milkdown/crepe';
import { replaceAll, $remark, $prose } from '@milkdown/kit/utils';
import { visit } from 'unist-util-visit';
import { editorViewCtx, commandsCtx } from '@milkdown/kit/core';

const remarkFixImageNodes = $remark('remarkFixImageNodes', () => () => (ast) => {
  visit(ast, (node, index, parent) => {
    if (node.type === 'paragraph' && node.children && parent && parent.children && typeof index === 'number') {
      const imgChildren = node.children.filter(c => c.type === 'image' || c.type === 'image-block');
      const nonImgNonEmpty = node.children.filter(c => c.type !== 'image' && c.type !== 'image-block' && (c.type !== 'text' || (c.value && c.value.trim().length > 0)));
      if (imgChildren.length > 0 && nonImgNonEmpty.length === 0) {
        const newNodes = imgChildren.map(img => ({
          type: 'image-block',
          url: img.url || '',
          alt: (img.alt && !Number.isNaN(Number(img.alt))) ? img.alt : '1',
          title: img.title || ''
        }));
        parent.children.splice(index, 1, ...newNodes);
        return index + newNodes.length;
      }
    }
    if (node.type === 'image' || node.type === 'image-block') {
      if (node.title == null) node.title = '';
      if (node.alt == null) node.alt = '';
      if (node.url == null) node.url = '';
    }
  });
});

const autoInlineCodePlugin = $prose(() => new Plugin({
  key: new PluginKey('autoInlineCodePlugin'),
  props: {
    handlePaste(view, event, slice) {
      if (!view || !view.state) return false;
      const { state, dispatch } = view;
      const { from, to, empty } = getEffectiveSelection(view);

      const items = event.clipboardData ? event.clipboardData.items : null;
      if (items && items.length > 0) {
        for (let i = 0; i < items.length; i++) {
          const item = items[i];
          if (item.type && item.type.indexOf('image') !== -1) {
            const file = item.getAsFile();
            if (file) {
              uploadAndInsertImage(file);
              return true;
            }
          }
        }
      }

      const rawText = event.clipboardData ? event.clipboardData.getData('text/plain') : '';
      if (!rawText) return false;

      const trimmed = rawText.trim();

      // 1. URL pasted on non-empty selection -> convert to markdown link [selection](url)
      const isUrl = /^https?:\/\/\S+$/i.test(trimmed) || /^www\.\S+\.\S+$/i.test(trimmed);
      if (isUrl && !empty) {
        let cleanUrl = trimmed;
        if (!cleanUrl.includes('://') && !cleanUrl.startsWith('mailto:') && !cleanUrl.startsWith('#')) {
          cleanUrl = 'https://' + cleanUrl;
        }
        const linkType = state.schema.marks.link;
        if (linkType) {
          const mark = linkType.create({ href: cleanUrl });
          const selectedText = state.doc.textBetween(from, to);
          const tr = state.tr.replaceWith(from, to, state.schema.text(selectedText, [mark]));
          dispatch(tr.scrollIntoView());
          return true;
        }
      }

      // 2. Cursor placed between backticks: `|` -> replace `|` with `codeText`
      const codeMarkType = state.schema.marks.inlineCode || state.schema.marks.code_inline;
      if (codeMarkType) {
        const $from = state.doc.resolve(from);
        const $to = state.doc.resolve(to);
        if ($from.parent && $from.parent.isTextblock && !($from.parent.type.name.includes('code_block') || $from.parent.type.name.includes('code_bracket'))) {
          const textBefore = $from.parent.textBetween(0, $from.parentOffset);
          const textAfter = $to.parent.textBetween($to.parentOffset, $to.parent.content.size);

          if (textBefore.endsWith('`') && textAfter.startsWith('`')) {
            let codeText = rawText;
            if (codeText.startsWith('`') && codeText.endsWith('`') && codeText.length >= 2) {
              codeText = codeText.slice(1, -1);
            }
            const mark = codeMarkType.create();
            const replaceFrom = from - 1;
            const replaceTo = to + 1;
            const tr = state.tr.replaceWith(replaceFrom, replaceTo, state.schema.text(codeText, [mark]));
            dispatch(tr.scrollIntoView());
            return true;
          }

          // Pasted text itself is enclosed in single backticks: `hello world`
          if (trimmed.startsWith('`') && trimmed.endsWith('`') && !trimmed.startsWith('```') && trimmed.length >= 2 && !trimmed.includes('\n')) {
            const codeText = trimmed.slice(1, -1);
            const mark = codeMarkType.create();
            const tr = state.tr.replaceWith(from, to, state.schema.text(codeText, [mark]));
            dispatch(tr.scrollIntoView());
            return true;
          }
        }
      }

      return false;
    }
  },
  appendTransaction(transactions, oldState, newState) {
    if (isUpdatingFromHost) return null;
    const docChanged = transactions.some(tr => tr.docChanged);
    if (!docChanged) return null;

    const codeMarkType = newState.schema.marks.inlineCode || newState.schema.marks.code_inline;
    if (!codeMarkType) return null;

    const matches = [];
    newState.doc.descendants((node, pos, parent) => {
      if (!node.isText) return;
      if (parent && (parent.type.name.includes('code_block') || parent.type.name.includes('code_bracket') || parent.type.spec?.code)) return;
      if (node.marks && node.marks.some(m => m.type === codeMarkType || m.type.name === 'inlineCode' || m.type.name === 'code_inline')) return;

      const text = node.text;
      if (!text || !text.includes('`')) return;

      const regex = /`([^`\r\n]+)`/g;
      let match;
      while ((match = regex.exec(text)) !== null) {
        matches.push({
          from: pos + match.index,
          to: pos + match.index + match[0].length,
          code: match[1]
        });
      }
    });

    if (matches.length === 0) return null;

    const tr = newState.tr;
    for (let i = matches.length - 1; i >= 0; i--) {
      const m = matches[i];
      const mark = codeMarkType.create();
      tr.replaceWith(m.from, m.to, newState.schema.text(m.code, [mark]));
    }

    return tr.docChanged ? tr : null;
  }
}));

import { Decoration, DecorationSet } from '@milkdown/kit/prose/view';

const headingFoldPlugin = $prose(() => {
  const key = new PluginKey('headingFoldPlugin');

  return new Plugin({
    key,
    state: {
      init() {
        return { folded: new Set() };
      },
      apply(tr, value, oldState, newState) {
        const meta = tr.getMeta(key);
        if (meta && meta.togglePos !== undefined) {
          const newFolded = new Set(value.folded);
          if (newFolded.has(meta.togglePos)) {
            newFolded.delete(meta.togglePos);
          } else {
            newFolded.add(meta.togglePos);
          }
          return { folded: newFolded };
        }
        if (tr.docChanged) {
          const newFolded = new Set();
          for (const pos of value.folded) {
            const mapped = tr.mapping.map(pos);
            if (mapped < newState.doc.content.size) {
              const node = newState.doc.nodeAt(mapped);
              if (node && node.type.name === 'heading') {
                newFolded.add(mapped);
              }
            }
          }
          return { folded: newFolded };
        }
        return value;
      }
    },
    props: {
      decorations(state) {
        const { folded } = this.getState(state);
        const decos = [];
        const headings = [];

        state.doc.descendants((node, pos) => {
          if (node.type.name === 'heading') {
            headings.push({ pos, level: node.attrs.level || 1, node, size: node.nodeSize });
          }
        });

        headings.forEach((h, idx) => {
          if (!h.node || h.node.textContent.trim().length === 0) return;

          const isFolded = folded.has(h.pos);
          const btn = document.createElement('span');
          btn.className = 'heading-fold-btn' + (isFolded ? ' is-folded' : '');
          btn.contentEditable = 'false';
          btn.innerHTML = '<svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><path d="M7.41 8.59L12 13.17l4.59-4.58L18 10l-6 6-6-6 1.41-1.41z"/></svg>';
          btn.title = isFolded ? 'Click to expand section' : 'Click to collapse section';
          btn.dataset.headingPos = String(h.pos);

          const toggleAction = (e) => {
            e.preventDefault();
            e.stopPropagation();
            const view = getEditorView();
            if (view) {
              view.dispatch(view.state.tr.setMeta(key, { togglePos: h.pos }));
            }
          };

          btn.addEventListener('pointerdown', toggleAction);
          btn.addEventListener('click', toggleAction);

          decos.push(Decoration.widget(h.pos + 1, btn, {
            side: -1,
            ignoreSelection: true,
            stopEvent: () => true,
            key: 'fold_btn_' + h.pos
          }));

          if (isFolded) {
            decos.push(Decoration.node(h.pos, h.pos + h.size, { class: 'is-folded' }));

            const badge = document.createElement('span');
            badge.className = 'heading-fold-badge';
            badge.contentEditable = 'false';
            badge.textContent = '···';
            badge.title = 'Click to expand section';
            badge.dataset.headingPos = String(h.pos);
            badge.addEventListener('pointerdown', toggleAction);
            badge.addEventListener('click', toggleAction);

            decos.push(Decoration.widget(h.pos + h.size - 1, badge, {
              side: 1,
              ignoreSelection: true,
              stopEvent: () => true,
              key: 'fold_badge_' + h.pos
            }));

            let foldEnd = state.doc.content.size;
            for (let j = idx + 1; j < headings.length; j++) {
              if (headings[j].level <= h.level) {
                foldEnd = headings[j].pos;
                break;
              }
            }

            const foldStart = h.pos + h.size;
            if (foldEnd > foldStart) {
              state.doc.nodesBetween(foldStart, foldEnd, (childNode, childPos) => {
                if (childPos >= foldStart && childPos < foldEnd && childNode.isBlock) {
                  decos.push(Decoration.node(childPos, childPos + childNode.nodeSize, { class: 'heading-folded-hidden' }));
                }
              });
            }
          }
        });

        return DecorationSet.create(state.doc, decos);
      },
      handleClick(view, pos, event) {
        const foldBtn = event.target.closest('.heading-fold-btn, .heading-fold-badge');
        if (foldBtn) {
          event.preventDefault();
          event.stopPropagation();
          const hPosStr = foldBtn.dataset.headingPos;
          if (hPosStr != null) {
            const hPos = parseInt(hPosStr, 10);
            view.dispatch(view.state.tr.setMeta(key, { togglePos: hPos }));
            return true;
          }
        }
        return false;
      }
    }
  });
});

import { createTable, strikethroughSchema } from '@milkdown/kit/preset/gfm';
import { clearTextInCurrentBlockCommand, addBlockTypeCommand, selectTextNearPosCommand, codeBlockSchema, hrSchema, setBlockTypeCommand, linkSchema, strongSchema, emphasisSchema, inlineCodeSchema } from '@milkdown/kit/preset/commonmark';
import { Selection, Plugin, PluginKey } from '@milkdown/kit/prose/state';
import { liftListItem } from '@milkdown/kit/prose/schema-list';
import { lift } from '@milkdown/kit/prose/commands';
import { liftTarget } from '@milkdown/kit/prose/transform';
import '@milkdown/crepe/theme/common/style.css';
import '@milkdown/crepe/theme/frame-dark.css';

let crepeInstance = null;
let editorView = null;
let isUpdatingFromHost = false;
let hasReceivedInitialContent = false;

function getEditorView() {
  if (editorView) return editorView;
  if (crepeInstance && crepeInstance.editor) {
    try {
      crepeInstance.editor.action((ctx) => {
        editorView = ctx.get(editorViewCtx);
      });
    } catch (e) {}
  }
  return editorView;
}

function handleWordStyleBackspace(view) {
  if (!view || !view.state) return false;
  const { state, dispatch } = view;
  const { selection } = state;
  if (!selection || !selection.empty) return false;

  const { $from } = selection;
  // Only handle when cursor is at the very beginning of the textblock (offset 0)
  if ($from.parentOffset !== 0) return false;

  const schema = state.schema;
  const listItemType = schema.nodes.list_item;
  if (!listItemType) return false;

  let listItemDepth = -1;
  for (let d = $from.depth; d > 0; d--) {
    if ($from.node(d).type === listItemType) {
      listItemDepth = d;
      break;
    }
  }

  const bqType = schema.nodes.blockquote;
  if (listItemDepth === -1 && bqType) {
    for (let d = $from.depth; d > 0; d--) {
      if ($from.node(d).type === bqType) {
        if (lift(state, dispatch)) {
          return true;
        }
        break;
      }
    }
    return false;
  }

  if (listItemDepth === -1) {
    return false;
  }

  // Cursor is at offset 0 of a block inside a list item!
  // In Microsoft Word / Notion:
  // 1. If it's a sub-item in a nested list, lift it to the parent list (outdent).
  // 2. If it's in a top-level list, lift it out of the list into a normal paragraph (removes index/bullet).
  // 3. If it's a subsequent paragraph inside a list item, split it out and lift it to a normal paragraph.
  // NEVER join into the line above!
  try {
    const indexInItem = $from.index(listItemDepth);
    if (indexInItem > 0) {
      const splitPos = $from.before(listItemDepth + 1);
      const tr = state.tr.split(splitPos, 1);
      const newState = state.apply(tr);
      if (liftListItem(listItemType)(newState, dispatch)) {
        return true;
      }
      dispatch(tr.scrollIntoView());
      return true;
    }

    if (liftListItem(listItemType)(state, dispatch)) {
      return true;
    }

    if (lift(state, dispatch)) {
      return true;
    }

    const range = $from.blockRange($from);
    if (range) {
      const target = liftTarget(range);
      if (target != null) {
        dispatch(state.tr.lift(range, target).scrollIntoView());
        return true;
      }
    }
  } catch (err) {
    console.error('[handleWordStyleBackspace error]', err);
  }

  return false;
}

function handleWordStyleEnter(view) {
  if (!view || !view.state) return false;
  const { state, dispatch } = view;
  const { selection } = state;
  if (!selection || !selection.empty) return false;

  const { $from } = selection;
  if ($from.parent && $from.parent.type.name === 'heading') {
    if ($from.parentOffset === 0) {
      const pType = state.schema.nodes.paragraph;
      if (!pType) return false;

      const headingPos = $from.before();
      const emptyParagraph = pType.create();
      const tr = state.tr.insert(headingPos, emptyParagraph);

      const newPos = headingPos + emptyParagraph.nodeSize + 1;
      const sel = Selection.near(tr.doc.resolve(newPos));
      tr.setSelection(sel).scrollIntoView();

      dispatch(tr);
      return true;
    }
  }

  return false;
}

// Keyboard shortcut forwarding & browser hotkey prevention
window.addEventListener('keydown', (e) => {
  if (e.key === 'Enter' && !e.ctrlKey && !e.altKey && !e.metaKey && !e.shiftKey) {
    const view = getEditorView();
    if (view && handleWordStyleEnter(view)) {
      e.preventDefault();
      e.stopPropagation();
      e.stopImmediatePropagation();
      return;
    }
  }

  if (e.key === 'Backspace' && !e.ctrlKey && !e.altKey && !e.metaKey) {
    const view = getEditorView();
    if (view && handleWordStyleBackspace(view)) {
      e.preventDefault();
      e.stopPropagation();
      e.stopImmediatePropagation();
      return;
    }
  }

  if (e.ctrlKey && !e.shiftKey && !e.altKey) {
    const k = e.key.toLowerCase();
    if (k === 's') {
      e.preventDefault();
      sendMessage({ type: 'shortcut', action: 'save' });
      return;
    }
    if (k === 'n') {
      e.preventDefault();
      sendMessage({ type: 'shortcut', action: 'newNote' });
      return;
    }
    if (k === 'f') {
      e.preventDefault();
      sendMessage({ type: 'shortcut', action: 'search' });
      return;
    }
    if (k === 'w') {
      e.preventDefault();
      sendMessage({ type: 'shortcut', action: 'closeTab' });
      return;
    }
    if (k === 'k') {
      e.preventDefault();
      showLinkDialog();
      return;
    }
    if (k === 'r') {
      e.preventDefault();
      return;
    }
  } else if (e.ctrlKey && e.shiftKey && !e.altKey) {
    if (e.key.toLowerCase() === 'n') {
      e.preventDefault();
      sendMessage({ type: 'shortcut', action: 'newFolder' });
      return;
    }
  } else if (e.key === 'F5') {
    e.preventDefault();
  } else if (e.key === 'Escape') {
    hideContextMenu();
    hideLinkDialog();
  } else if (e.key === '`' && !e.ctrlKey && !e.altKey && !e.metaKey) {
    const view = getEditorView();
    if (view && view.state) {
      const { from, to, empty } = getEffectiveSelection(view);
      if (!empty) {
        const codeMarkType = view.state.schema.marks.inlineCode || view.state.schema.marks.code_inline;
        if (codeMarkType) {
          e.preventDefault();
          e.stopPropagation();
          const selectedText = view.state.doc.textBetween(from, to);
          const mark = codeMarkType.create();
          const tr = view.state.tr.replaceWith(from, to, view.state.schema.text(selectedText, [mark]));
          view.dispatch(tr.scrollIntoView());
          return;
        }
      }
    }
  }
}, true);

async function init() {
  const root = document.getElementById('editor');
  if (!root) return;

  const features = {};
  if (Crepe && Crepe.Feature && Crepe.Feature.BlockEdit) {
    features[Crepe.Feature.BlockEdit] = false;
  }

  crepeInstance = new Crepe({
    root,
    defaultValue: '',
    features,
    featureConfigs: {
      [Crepe.Feature.ImageBlock]: {
        proxyDomURL: proxyDomURL,
      },
    },
  });

  // Listen to markdown changes and send back to host
  crepeInstance.on((listener) => {
    listener.markdownUpdated((ctx, markdown, prevMarkdown) => {
      if (isUpdatingFromHost) return;
      if (!hasReceivedInitialContent && !markdown) return;
      hasReceivedInitialContent = true;
      sendMessage({
        type: 'contentChanged',
        markdown: markdown,
      });
    });
  });

  try {
    crepeInstance.editor.use(remarkFixImageNodes);
    crepeInstance.editor.use(autoInlineCodePlugin);
    crepeInstance.editor.use(headingFoldPlugin);
    await crepeInstance.create();
    crepeInstance.editor.action((ctx) => {
      editorView = ctx.get(editorViewCtx);
    });
    applySpellCheck(false);
  } catch (err) {
    console.error('Failed to create Crepe editor:', err);
  }

  // Setup right-click & two-finger tap context formatting menu
  setupContextMenu();

  // Setup inline link insert & edit dialog
  setupLinkDialog();

  // Global methods accessible by host C# InvokeScript
  window.setMarkdown = (markdown) => {
    hasReceivedInitialContent = true;
    if (!crepeInstance) return;
    const current = crepeInstance.getMarkdown();
    if ((current || '').trim() === (markdown || '').trim()) {
      return;
    }

    const cleanMd = (markdown || '').replace(/(!\[.*?\]\([^\n\r]*?\))\r?\n(?=!\[)/g, '$1\n\n');

    // Pre-scan markdown for images to request them early from host
    const md = cleanMd || '';
    const imgRegex = /!\[.*?\]\(\s*<?([^\s>)]+)>?\s*\)/g;
    let match;
    while ((match = imgRegex.exec(md)) !== null) {
      const rawSrc = match[1];
      const clean = cleanImageSrc(rawSrc);
      if (clean && !clean.startsWith('data:') && !clean.startsWith('http://') && !clean.startsWith('https://')) {
        const fileName = clean.split(/[/\\]/).pop();
        if (!imageBase64Cache[clean] && !(fileName && imageBase64Cache[fileName])) {
          sendMessage({ type: 'loadImage', src: clean });
        }
      }
    }

    isUpdatingFromHost = true;
    try {
      crepeInstance.editor.action(replaceAll(cleanMd));
      setTimeout(resolveAllImagesInDOM, 60);
      setTimeout(() => {
        if (window.updateHeadingFolds) window.updateHeadingFolds();
      }, 30);
    } catch (e) {
      console.error('[setMarkdown] Failed to replace markdown:', e);
    } finally {
      setTimeout(() => {
        isUpdatingFromHost = false;
      }, 150);
    }
  };

  window.getMarkdown = () => {
    return crepeInstance ? crepeInstance.getMarkdown() : '';
  };

  window.focusEditor = () => {
    const el = document.querySelector('.milkdown .ProseMirror');
    if (el) el.focus();
  };

  window.scrollToHeading = (headingText, level) => {
    if (!headingText) return;
    const target = headingText.trim().toLowerCase();

    // 1. Try finding heading via ProseMirror document node traversal
    if (crepeInstance) {
      try {
        const view = crepeInstance.editor.ctx.get(editorViewCtx);
        if (view) {
          let targetPos = -1;
          view.state.doc.descendants((node, pos) => {
            if (node.type.name === 'heading') {
              const nodeText = (node.textContent || '').trim().toLowerCase();
              if (nodeText === target || (target.length > 2 && (nodeText.includes(target) || target.includes(nodeText)))) {
                if (targetPos === -1) {
                  targetPos = pos;
                }
              }
            }
          });

          if (targetPos >= 0) {
            const safePos = Math.min(targetPos + 1, view.state.doc.content.size);
            const $pos = view.state.doc.resolve(safePos);
            const sel = Selection.near($pos);
            const tr = view.state.tr.setSelection(sel).scrollIntoView();
            view.dispatch(tr);
            view.focus();

            const dom = view.nodeDOM(targetPos);
            if (dom) {
              if (dom.scrollIntoView) {
                dom.scrollIntoView({ behavior: 'smooth', block: 'center' });
              }
              dom.style.transition = 'background-color 0.25s';
              dom.style.backgroundColor = 'rgba(59, 130, 246, 0.3)';
              setTimeout(() => { dom.style.backgroundColor = ''; }, 1200);
              return;
            }
          }
        }
      } catch (e) {
        console.error('Error in ProseMirror scrollToHeading:', e);
      }
    }

    // 2. Fallback: Search all heading elements in DOM
    const editorRoot = document.getElementById('editor') || document;
    const headings = Array.from(editorRoot.querySelectorAll('h1, h2, h3, h4, h5, h6'));
    let targetEl = headings.find(h => {
      const text = (h.textContent || '').trim().toLowerCase();
      return text === target || text.includes(target) || target.includes(text);
    });

    if (targetEl) {
      targetEl.scrollIntoView({ behavior: 'smooth', block: 'center' });
      targetEl.style.transition = 'background-color 0.25s';
      targetEl.style.backgroundColor = 'rgba(59, 130, 246, 0.3)';
      setTimeout(() => { targetEl.style.backgroundColor = ''; }, 1200);

      try {
        const selection = window.getSelection();
        const range = document.createRange();
        range.selectNodeContents(targetEl);
        range.collapse(true);
        selection.removeAllRanges();
        selection.addRange(range);
        targetEl.focus();
      } catch {}
    }
  };

  window.setSpellCheck = (enabled) => {
    applySpellCheck(enabled);
  };

  // If C# host invoked setMarkdown before Crepe finished creating, apply it now
  if (window.__pendingMarkdown !== undefined) {
    const pending = window.__pendingMarkdown;
    delete window.__pendingMarkdown;
    window.setMarkdown(pending);
  }

  // Signal C# host that editor is loaded and ready
  sendMessage({ type: 'ready' });
}

function applySpellCheck(enabled) {
  const isEnabled = !!enabled;
  const pm = document.querySelector('.ProseMirror');
  if (pm) pm.setAttribute('spellcheck', isEnabled ? 'true' : 'false');
  const root = document.getElementById('editor');
  if (root) root.setAttribute('spellcheck', isEnabled ? 'true' : 'false');
  const container = document.getElementById('editor-container');
  if (container) container.setAttribute('spellcheck', isEnabled ? 'true' : 'false');
  document.body.setAttribute('spellcheck', isEnabled ? 'true' : 'false');
}

let contextMenuX = 0;
let contextMenuY = 0;

// Custom Right-Click & Touchpad Context Formatting Menu
function setupContextMenu() {
  const menu = document.getElementById('format-menu');
  if (!menu) return;

  window.addEventListener('contextmenu', (e) => {
    e.preventDefault();
    contextMenuX = e.clientX;
    contextMenuY = e.clientY;
    showContextMenu(e.clientX, e.clientY);
  });

  window.addEventListener('pointerdown', (e) => {
    if (!menu.contains(e.target)) {
      hideContextMenu();
    }
  });

  menu.querySelectorAll('[data-action]').forEach((btn) => {
    btn.addEventListener('click', (e) => {
      e.stopPropagation();
      const action = btn.getAttribute('data-action');
      executeAction(action);
      hideContextMenu();
    });
  });
}

function showContextMenu(x, y) {
  const menu = document.getElementById('format-menu');
  if (!menu) return;

  const sel = window.getSelection() ? window.getSelection().toString().trim() : '';
  const ignoreBtn = document.getElementById('menu-ignore-word');
  const ignoreDiv = document.getElementById('menu-ignore-divider');
  if (ignoreBtn) {
    if (sel && sel.length > 0 && sel.length <= 40 && !sel.includes('\n')) {
      ignoreBtn.style.display = 'flex';
      if (ignoreDiv) ignoreDiv.style.display = 'block';
      const label = ignoreBtn.querySelector('.label');
      if (label) label.textContent = `Ignore "${sel.length > 14 ? sel.substring(0, 14) + '...' : sel}"`;
      ignoreBtn.dataset.word = sel;
    } else {
      ignoreBtn.style.display = 'none';
      if (ignoreDiv) ignoreDiv.style.display = 'none';
    }
  }

  menu.style.display = 'block';

  const menuWidth = 230;
  const menuHeight = menu.offsetHeight || 380;
  const posX = (x + menuWidth > window.innerWidth) ? Math.max(10, window.innerWidth - menuWidth - 10) : x;
  const posY = (y + menuHeight > window.innerHeight) ? Math.max(10, window.innerHeight - menuHeight - 10) : y;

  menu.style.left = posX + 'px';
  menu.style.top = posY + 'px';
}

function hideContextMenu() {
  const menu = document.getElementById('format-menu');
  if (menu) menu.style.display = 'none';
}

function executeAction(action) {
  if (!crepeInstance) return;
  const view = crepeInstance.editor.ctx.get(editorViewCtx);
  if (!view) return;

  switch (action) {
    case 'h1':
      insertOrWrapBlock('# ');
      break;
    case 'h2':
      insertOrWrapBlock('## ');
      break;
    case 'h3':
      insertOrWrapBlock('### ');
      break;
    case 'h4':
      insertOrWrapBlock('#### ');
      break;
    case 'h5':
      insertOrWrapBlock('##### ');
      break;
    case 'h6':
      insertOrWrapBlock('###### ');
      break;
    case 'todo':
      insertOrWrapBlock('- [ ] ');
      break;
    case 'bullet':
      insertOrWrapBlock('- ');
      break;
    case 'number':
      insertOrWrapBlock('1. ');
      break;
    case 'quote':
      insertOrWrapBlock('> ');
      break;
    case 'code':
      try {
        const ctx = crepeInstance.editor.ctx;
        const commands = ctx.get(commandsCtx);
        const codeBlock = codeBlockSchema.type(ctx);
        commands.call(clearTextInCurrentBlockCommand.key);
        commands.call(setBlockTypeCommand.key, {
          nodeType: codeBlock,
        });
        view.focus();
      } catch (e) {
        insertText('\n```\n\n```\n');
      }
      break;
    case 'table':
      try {
        const ctx = crepeInstance.editor.ctx;
        const commands = ctx.get(commandsCtx);
        commands.call(clearTextInCurrentBlockCommand.key);
        const { from } = view.state.selection;
        commands.call(addBlockTypeCommand.key, {
          nodeType: createTable(ctx, 3, 3),
        });
        commands.call(selectTextNearPosCommand.key, {
          pos: from,
        });
        view.focus();
      } catch (err) {
        insertText('\n| Column 1 | Column 2 | Column 3 |\n| --- | --- | --- |\n| Cell 1 | Cell 2 | Cell 3 |\n');
      }
      break;
    case 'divider':
      try {
        const ctx = crepeInstance.editor.ctx;
        const commands = ctx.get(commandsCtx);
        const hr = hrSchema.type(ctx);
        commands.call(clearTextInCurrentBlockCommand.key);
        commands.call(addBlockTypeCommand.key, {
          nodeType: hr,
        });
        view.focus();
      } catch (err) {
        insertText('\n---\n');
      }
      break;
    case 'link':
      showLinkDialog(contextMenuX, contextMenuY);
      break;
    case 'bold':
      toggleMark('strong', strongSchema);
      break;
    case 'italic':
      toggleMark('em', emphasisSchema);
      break;
    case 'strike':
      toggleMark('strike_through', strikethroughSchema);
      break;
    case 'inline-code':
      toggleMark('code_inline', inlineCodeSchema);
      break;
    case 'ignore-word':
      const ignoreBtn = document.getElementById('menu-ignore-word');
      const wordToIgnore = (ignoreBtn && ignoreBtn.dataset.word) || (window.getSelection() ? window.getSelection().toString().trim() : '');
      if (wordToIgnore) {
        sendMessage({ type: 'ignoreWord', word: wordToIgnore });
      }
      break;
  }
}

function toggleMark(markName, schemaHelper) {
  if (!crepeInstance) return;
  const view = crepeInstance.editor.ctx.get(editorViewCtx);
  if (!view) return;
  const { state, dispatch } = view;
  const { from, to, empty } = getEffectiveSelection(view);
  if (empty) return;

  let markType = state.schema.marks[markName];
  if (!markType && (markName === 'code_inline' || markName === 'inline-code')) markType = state.schema.marks.inlineCode;
  if (!markType && markName === 'inlineCode') markType = state.schema.marks.code_inline;
  if (!markType && markName === 'em') markType = state.schema.marks.emphasis;
  if (!markType && schemaHelper) {
    try {
      markType = schemaHelper.type(crepeInstance.editor.ctx);
    } catch {}
  }
  if (!markType) return;

  const hasMark = state.doc.rangeHasMark(from, to, markType);
  if (hasMark) {
    dispatch(state.tr.removeMark(from, to, markType));
  } else {
    dispatch(state.tr.addMark(from, to, markType.create()));
  }
  view.focus();
}

function getLinkAtSelection(state) {
  if (!state || !state.selection) return null;
  const { from, to, empty } = state.selection;
  const linkType = state.schema.marks.link;
  if (!linkType) return null;

  if (empty) {
    const $pos = state.selection.$from;
    const marks = $pos.marks();
    const linkMark = marks.find(m => m.type === linkType);
    if (!linkMark) return null;

    let start = $pos.pos;
    let end = $pos.pos;
    const parent = $pos.parent;
    const offset = $pos.parentOffset;
    parent.forEach((child, childOffset) => {
      const childEnd = childOffset + child.nodeSize;
      if (offset >= childOffset && offset <= childEnd) {
        if (child.marks && child.marks.some(m => m.type === linkType)) {
          start = $pos.start() + childOffset;
          end = start + child.nodeSize;
        }
      }
    });
    return {
      mark: linkMark,
      href: linkMark.attrs.href,
      text: state.doc.textBetween(start, end),
      from: start,
      to: end
    };
  } else {
    let found = null;
    state.doc.nodesBetween(from, to, (node, pos) => {
      const mark = node.marks && node.marks.find(m => m.type === linkType);
      if (mark && !found) {
        found = {
          mark,
          href: mark.attrs.href,
          text: state.doc.textBetween(from, to),
          from,
          to
        };
      }
    });
    return found;
  }
}

function getEffectiveSelection(view) {
  if (!view || !view.state) return { from: 0, to: 0, empty: true };
  let { from, to, empty } = view.state.selection;
  if (empty) {
    const domSel = window.getSelection();
    if (domSel && domSel.rangeCount > 0 && !domSel.isCollapsed) {
      try {
        const range = domSel.getRangeAt(0);
        const startPos = view.posAtDOM(range.startContainer, range.startOffset);
        const endPos = view.posAtDOM(range.endContainer, range.endOffset);
        if (startPos != null && endPos != null && startPos !== endPos) {
          from = Math.min(startPos, endPos);
          to = Math.max(startPos, endPos);
          empty = false;
        }
      } catch {}
    }
  }
  return { from, to, empty };
}

function applyLink(url, text) {
  const view = getEditorView();
  if (!view) return;
  const { state, dispatch } = view;
  const linkType = state.schema.marks.link;
  if (!linkType) return;

  let cleanUrl = (url || '').trim();
  if (!cleanUrl) return;

  if (!cleanUrl.includes('://') && !cleanUrl.startsWith('mailto:') && !cleanUrl.startsWith('#')) {
    cleanUrl = 'https://' + cleanUrl;
  }

  const mark = linkType.create({ href: cleanUrl });
  const existing = getLinkAtSelection(state);
  const { from, to, empty } = getEffectiveSelection(view);

  const tr = state.tr;
  if (existing) {
    const displayText = (text && text.trim()) ? text.trim() : (existing.text || cleanUrl);
    tr.replaceWith(existing.from, existing.to, state.schema.text(displayText, [mark]));
  } else if (empty) {
    const displayText = (text && text.trim()) ? text.trim() : cleanUrl;
    tr.replaceWith(from, to, state.schema.text(displayText, [mark]));
  } else {
    const selectedText = state.doc.textBetween(from, to);
    const displayText = (text && text.trim()) ? text.trim() : selectedText;
    tr.replaceWith(from, to, state.schema.text(displayText, [mark]));
  }
  dispatch(tr.scrollIntoView());
  view.focus();
}

function removeLinkAtSelection() {
  const view = getEditorView();
  if (!view) return;
  const { state, dispatch } = view;
  const linkType = state.schema.marks.link;
  if (!linkType) return;

  const existing = getLinkAtSelection(state);
  if (existing) {
    dispatch(state.tr.removeMark(existing.from, existing.to, linkType).scrollIntoView());
  } else {
    const { from, to } = getEffectiveSelection(view);
    dispatch(state.tr.removeMark(from, to, linkType).scrollIntoView());
  }
  view.focus();
}

function showLinkDialog(posX, posY) {
  hideContextMenu();
  const dialog = document.getElementById('link-dialog');
  if (!dialog) return;

  const view = getEditorView();
  if (!view) return;

  const textInput = document.getElementById('link-text-input');
  const urlInput = document.getElementById('link-url-input');
  const titleEl = document.getElementById('link-dialog-title');
  const applyBtn = document.getElementById('link-dialog-apply');
  const removeBtn = document.getElementById('link-dialog-remove');

  const existing = getLinkAtSelection(view.state);
  const { from, to, empty } = getEffectiveSelection(view);

  if (existing) {
    if (titleEl) titleEl.textContent = 'Edit Link';
    if (applyBtn) applyBtn.textContent = 'Save';
    if (removeBtn) removeBtn.style.display = 'inline-block';
    if (textInput) textInput.value = existing.text || '';
    if (urlInput) urlInput.value = existing.href || '';
  } else {
    if (titleEl) titleEl.textContent = 'Insert Link';
    if (applyBtn) applyBtn.textContent = 'Insert';
    if (removeBtn) removeBtn.style.display = 'none';
    const selectedText = empty ? '' : view.state.doc.textBetween(from, to);
    if (textInput) textInput.value = selectedText;
    if (urlInput) urlInput.value = '';
  }

  dialog.style.display = 'block';

  const dialogWidth = 320;
  const dialogHeight = 220;
  let left = posX;
  let top = posY;

  if (left == null || top == null) {
    try {
      const coords = view.coordsAtPos(from);
      left = coords.left;
      top = coords.bottom + 8;
    } catch {
      left = Math.max(20, (window.innerWidth - dialogWidth) / 2);
      top = Math.max(20, (window.innerHeight - dialogHeight) / 2);
    }
  }

  if (left + dialogWidth > window.innerWidth - 10) {
    left = Math.max(10, window.innerWidth - dialogWidth - 10);
  }
  if (top + dialogHeight > window.innerHeight - 10) {
    top = Math.max(10, top - dialogHeight - 40);
  }

  dialog.style.left = left + 'px';
  dialog.style.top = top + 'px';

  setTimeout(() => {
    if (textInput && textInput.value) {
      if (urlInput) {
        urlInput.focus();
        urlInput.select();
      }
    } else if (textInput) {
      textInput.focus();
    }
  }, 50);
}

function hideLinkDialog() {
  const dialog = document.getElementById('link-dialog');
  if (dialog) dialog.style.display = 'none';
}

function setupLinkDialog() {
  const dialog = document.getElementById('link-dialog');
  if (!dialog) return;

  const textInput = document.getElementById('link-text-input');
  const urlInput = document.getElementById('link-url-input');
  const applyBtn = document.getElementById('link-dialog-apply');
  const cancelBtn = document.getElementById('link-dialog-cancel');
  const removeBtn = document.getElementById('link-dialog-remove');

  const onApply = () => {
    const url = urlInput ? urlInput.value : '';
    const text = textInput ? textInput.value : '';
    if (url && url.trim()) {
      applyLink(url, text);
    }
    hideLinkDialog();
  };

  if (applyBtn) {
    applyBtn.addEventListener('click', (e) => {
      e.stopPropagation();
      onApply();
    });
  }

  if (cancelBtn) {
    cancelBtn.addEventListener('click', (e) => {
      e.stopPropagation();
      hideLinkDialog();
      const view = getEditorView();
      if (view) view.focus();
    });
  }

  if (removeBtn) {
    removeBtn.addEventListener('click', (e) => {
      e.stopPropagation();
      removeLinkAtSelection();
      hideLinkDialog();
    });
  }

  const handleKey = (e) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      e.stopPropagation();
      onApply();
    } else if (e.key === 'Escape') {
      e.preventDefault();
      e.stopPropagation();
      hideLinkDialog();
      const view = getEditorView();
      if (view) view.focus();
    }
  };

  if (textInput) textInput.addEventListener('keydown', handleKey);
  if (urlInput) urlInput.addEventListener('keydown', handleKey);

  window.addEventListener('pointerdown', (e) => {
    if (dialog.style.display !== 'none' && !dialog.contains(e.target)) {
      hideLinkDialog();
    }
  });
}

function insertOrWrapBlock(prefix) {
  if (!crepeInstance) return;
  const view = crepeInstance.editor.ctx.get(editorViewCtx);
  if (!view) return;
  const { state, dispatch } = view;
  const $from = state.selection.$from;
  const lineStart = $from.start();
  const lineEnd = $from.end();
  const lineText = state.doc.textBetween(lineStart, lineEnd);

  const cleanedText = lineText.replace(/^(#{1,6}\s+|- \[[ xX]\]\s+|- \s+|\d+\.\s+|> \s+)/, '');
  const newText = prefix + cleanedText;

  const tr = state.tr.replaceWith(lineStart, lineEnd, state.schema.text(newText));
  dispatch(tr);
  view.focus();
}

function insertText(text) {
  if (!crepeInstance) return;
  const view = crepeInstance.editor.ctx.get(editorViewCtx);
  if (!view) return;
  const { state, dispatch } = view;
  dispatch(state.tr.replaceSelectionWith(state.schema.text(text)));
  view.focus();
}

function wrapSelection(before, after) {
  if (!crepeInstance) return;
  const view = crepeInstance.editor.ctx.get(editorViewCtx);
  if (!view) return;
  const { state, dispatch } = view;
  const { from, to } = state.selection;
  if (from === to) {
    dispatch(state.tr.replaceSelectionWith(state.schema.text(before + 'text' + after)));
  } else {
    const selected = state.doc.textBetween(from, to);
    dispatch(state.tr.replaceWith(from, to, state.schema.text(before + selected + after)));
  }
  view.focus();
}

function sendMessage(data) {
  const payload = typeof data === 'string' ? data : JSON.stringify(data);
  try {
    if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
      window.chrome.webview.postMessage(payload);
    } else if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.webview) {
      window.webkit.messageHandlers.webview.postMessage(payload);
    }
  } catch (err) {
    console.error('Error sending message to host:', err);
  }
}

function handleLinkClick(e) {
  const anchor = e.target.closest('a');
  if (!anchor) return;

  // Don't intercept clicks inside dialogs or context menu
  if (anchor.closest('.link-dialog') || anchor.closest('.format-menu')) return;

  const href = anchor.getAttribute('href');
  if (!href || href === '#' || href.startsWith('javascript:')) return;

  // If user selected text by dragging mouse, don't open the link
  const sel = window.getSelection();
  if (sel && sel.toString().trim().length > 0) return;

  e.preventDefault();
  e.stopPropagation();

  sendMessage({
    type: 'openUrl',
    url: href
  });
}

window.addEventListener('click', handleLinkClick, true);
window.addEventListener('auxclick', (e) => {
  if (e.button === 1) handleLinkClick(e);
}, true);

// Scan current block and convert literal `...` into inline_code mark (reverse index order)
function autoFormatInlineCode(view) {
  if (!view || !view.state) return false;
  const { state, dispatch } = view;
  const codeMarkType = state.schema.marks.inlineCode || state.schema.marks.code_inline;
  if (!codeMarkType) return false;

  const { $from } = state.selection;
  if (!$from || !$from.parent || !$from.parent.isTextblock) return false;

  const blockNode = $from.parent;
  if (blockNode.type.name === 'code_block' || blockNode.type.name === 'code_bracket') return false;

  const blockStart = $from.start();
  const matches = [];

  blockNode.forEach((child, childOffset) => {
    if (child.isText && !child.marks.some(m => m.type === codeMarkType || m.type.name === 'inlineCode' || m.type.name === 'code_inline')) {
      const text = child.text;
      const regex = /`([^`\r\n]+)`/g;
      let match;
      while ((match = regex.exec(text)) !== null) {
        const matchStart = blockStart + childOffset + match.index;
        const matchEnd = matchStart + match[0].length;
        matches.push({ from: matchStart, to: matchEnd, code: match[1] });
      }
    }
  });

  if (matches.length === 0) return false;

  let tr = state.tr;
  for (let i = matches.length - 1; i >= 0; i--) {
    const m = matches[i];
    const mark = codeMarkType.create();
    tr = tr.replaceWith(m.from, m.to, state.schema.text(m.code, [mark]));
  }

  if (tr.docChanged) {
    dispatch(tr);
    return true;
  }
  return false;
}

const imageBase64Cache = {};
const pendingImageRequests = new Set();
const pendingImageResolvers = {};

function cleanImageSrc(src) {
  if (!src) return '';
  let s = src.trim();
  if (s.startsWith('<') && s.endsWith('>') && s.length >= 2) {
    s = s.slice(1, -1).trim();
  }
  if (s.startsWith('file:///')) {
    s = s.slice(8);
  } else if (s.startsWith('file://')) {
    s = s.slice(7);
  }
  return s;
}

function proxyDomURL(rawSrc) {
  if (!rawSrc) return '';
  const clean = cleanImageSrc(rawSrc);
  if (clean.startsWith('data:') || clean.startsWith('http://') || clean.startsWith('https://')) {
    return rawSrc;
  }

  const fileName = clean.split(/[/\\]/).pop();

  if (imageBase64Cache[rawSrc]) return imageBase64Cache[rawSrc];
  if (imageBase64Cache[clean]) return imageBase64Cache[clean];
  if (fileName && imageBase64Cache[fileName]) return imageBase64Cache[fileName];

  return new Promise((resolve) => {
    if (!pendingImageResolvers[rawSrc]) pendingImageResolvers[rawSrc] = [];
    pendingImageResolvers[rawSrc].push(resolve);

    if (clean !== rawSrc) {
      if (!pendingImageResolvers[clean]) pendingImageResolvers[clean] = [];
      pendingImageResolvers[clean].push(resolve);
    }

    if (fileName && fileName !== clean) {
      if (!pendingImageResolvers[fileName]) pendingImageResolvers[fileName] = [];
      pendingImageResolvers[fileName].push(resolve);
    }

    if (!pendingImageRequests.has(clean)) {
      pendingImageRequests.add(clean);
      sendMessage({ type: 'loadImage', src: clean });
    }

    // Safety timeout in case image does not exist or host is slow
    setTimeout(() => {
      resolve(rawSrc);
    }, 5000);
  });
}

function resolveImageElement(img) {
  if (!img) return;
  const rawSrc = img.getAttribute('src');
  if (!rawSrc) return;

  const clean = cleanImageSrc(rawSrc);
  if (clean.startsWith('data:') || clean.startsWith('http://') || clean.startsWith('https://')) return;

  const fileName = clean.split(/[/\\]/).pop();

  if (imageBase64Cache[clean]) {
    if (img.src !== imageBase64Cache[clean]) {
      img.src = imageBase64Cache[clean];
    }
    return;
  }
  if (fileName && imageBase64Cache[fileName]) {
    if (img.src !== imageBase64Cache[fileName]) {
      img.src = imageBase64Cache[fileName];
    }
    return;
  }

  if (clean.startsWith('.assets/') || clean.startsWith('assets/') || clean.includes('.assets/')) {
    if (!pendingImageRequests.has(clean)) {
      pendingImageRequests.add(clean);
      sendMessage({ type: 'loadImage', src: clean });
    }
  }
}

function resolveAllImagesInDOM() {
  const imgs = document.querySelectorAll('.milkdown img, .ProseMirror img, img');
  imgs.forEach(resolveImageElement);
}

// Setup MutationObserver to automatically resolve images as they are mounted/rendered
const imgObserver = new MutationObserver((mutations) => {
  for (const m of mutations) {
    if (m.type === 'childList') {
      m.addedNodes.forEach((node) => {
        if (node.nodeType === Node.ELEMENT_NODE) {
          if (node.tagName === 'IMG') {
            resolveImageElement(node);
          } else {
            node.querySelectorAll && node.querySelectorAll('img').forEach(resolveImageElement);
          }
        }
      });
    } else if (m.type === 'attributes' && m.attributeName === 'src') {
      if (m.target && m.target.tagName === 'IMG') {
        resolveImageElement(m.target);
      }
    }
  }
});

if (document.body) {
  imgObserver.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['src'] });
} else {
  document.addEventListener('DOMContentLoaded', () => {
    imgObserver.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['src'] });
  });
}

function uploadAndInsertImage(file) {
  if (!file) return;
  const reader = new FileReader();
  reader.onload = (evt) => {
    const base64Data = evt.target.result;
    const reqId = 'img_' + Date.now() + '_' + Math.random().toString(36).substring(2, 8);
    sendMessage({
      type: 'saveImage',
      reqId: reqId,
      base64: base64Data,
      name: file.name || 'image.png',
      alt: 'image'
    });
  };
  reader.readAsDataURL(file);
}

window.addEventListener('error', (e) => {
  if (e.target && e.target.tagName === 'IMG') {
    resolveImageElement(e.target);
  }
}, true);

window.addEventListener('drop', (e) => {
  const files = e.dataTransfer ? e.dataTransfer.files : null;
  if (files && files.length > 0) {
    for (let i = 0; i < files.length; i++) {
      const file = files[i];
      if (file.type && file.type.startsWith('image/')) {
        e.preventDefault();
        e.stopPropagation();
        uploadAndInsertImage(file);
        return;
      }
    }
  }
}, true);

window.onImageSaved = (reqId, relPath, base64Data, altText) => {
  if (!relPath) return;
  const clean = cleanImageSrc(relPath);
  const fileName = clean.split(/[/\\]/).pop();

  if (base64Data) {
    imageBase64Cache[relPath] = base64Data;
    imageBase64Cache[clean] = base64Data;
    if (fileName) imageBase64Cache[fileName] = base64Data;
  }

  const view = getEditorView();
  if (view && view.state) {
    const { schema, tr } = view.state;
    const imageType = schema.nodes['image-block'] || schema.nodes.image;
    if (imageType) {
      const node = imageType.create({
        src: clean,
        alt: altText || 'image'
      });
      const newTr = tr.replaceSelectionWith(node).scrollIntoView();
      view.dispatch(newTr);
      view.focus();
    } else {
      insertText(`\n\n![${altText || 'image'}](${clean})\n\n`);
    }
  }

  setTimeout(resolveAllImagesInDOM, 50);
};

window.onImageLoaded = (src, base64Data) => {
  if (!src || !base64Data) return;
  const clean = cleanImageSrc(src);
  const fileName = clean.split(/[/\\]/).pop();

  imageBase64Cache[src] = base64Data;
  imageBase64Cache[clean] = base64Data;
  if (fileName) imageBase64Cache[fileName] = base64Data;

  const resolvers = [
    ...(pendingImageResolvers[src] || []),
    ...(pendingImageResolvers[clean] || []),
    ...(fileName ? (pendingImageResolvers[fileName] || []) : [])
  ];

  delete pendingImageResolvers[src];
  delete pendingImageResolvers[clean];
  if (fileName) delete pendingImageResolvers[fileName];

  resolvers.forEach((res) => {
    try {
      res(base64Data);
    } catch (err) {
      console.error('Error resolving image promise:', err);
    }
  });

  document.querySelectorAll('.milkdown img, .ProseMirror img, img').forEach((img) => {
    const rawAttr = img.getAttribute('src') || '';
    const cleanAttr = cleanImageSrc(rawAttr);
    const currentSrc = img.src || '';
    if (cleanAttr === src || cleanAttr === clean || (fileName && (cleanAttr.endsWith(fileName) || currentSrc.endsWith(fileName)))) {
      img.src = base64Data;
    }
  });
};

// Unified paste handler for Images, URLs, and Inline Code
function handlePaste(e) {
  // 0. Image paste detection
  const items = e.clipboardData ? e.clipboardData.items : null;
  if (items && items.length > 0) {
    for (let i = 0; i < items.length; i++) {
      const item = items[i];
      if (item.type && item.type.indexOf('image') !== -1) {
        const file = item.getAsFile();
        if (file) {
          e.preventDefault();
          e.stopPropagation();
          uploadAndInsertImage(file);
          return;
        }
      }
    }
  }

  const view = getEditorView();
  if (!view || !view.state) return;
  const { state, dispatch } = view;
  const { from, to, empty } = getEffectiveSelection(view);

  const rawText = e.clipboardData ? e.clipboardData.getData('text/plain') : '';
  if (!rawText) return;

  const trimmed = rawText.trim();

  // 1. URL pasted on selected text -> transform to [selectedText](url)
  const isUrl = /^https?:\/\/\S+$/i.test(trimmed) || /^www\.\S+\.\S+$/i.test(trimmed);
  if (isUrl && !empty) {
    let cleanUrl = trimmed;
    if (!cleanUrl.includes('://') && !cleanUrl.startsWith('mailto:') && !cleanUrl.startsWith('#')) {
      cleanUrl = 'https://' + cleanUrl;
    }

    const linkType = state.schema.marks.link;
    if (linkType) {
      e.preventDefault();
      e.stopPropagation();

      const mark = linkType.create({ href: cleanUrl });
      const selectedText = state.doc.textBetween(from, to);
      const tr = state.tr.replaceWith(from, to, state.schema.text(selectedText, [mark]));
      dispatch(tr.scrollIntoView());
      return;
    }
  }

  // 2. Inline Code Paste Scenarios:
  const codeMarkType = state.schema.marks.inlineCode || state.schema.marks.code_inline;
  if (codeMarkType) {
    // Scenario A: Cursor / selection is placed between existing literal backticks (e.g. `|` or `selected`)
    const $from = state.doc.resolve(from);
    const $to = state.doc.resolve(to);

    if ($from.parent && $from.parent.isTextblock) {
      const textBefore = $from.parent.textBetween(0, $from.parentOffset);
      const textAfter = $to.parent.textBetween($to.parentOffset, $to.parent.content.size);

      if (textBefore.endsWith('`') && textAfter.startsWith('`')) {
        // User pasted text between existing backticks `[cursor]` -> replace `[text]` with inline_code
        e.preventDefault();
        e.stopPropagation();
        e.stopImmediatePropagation();

        let codeText = rawText;
        if (codeText.startsWith('`') && codeText.endsWith('`') && codeText.length >= 2) {
          codeText = codeText.slice(1, -1);
        }

        const mark = codeMarkType.create();
        const replaceFrom = from - 1;
        const replaceTo = to + 1;
        const tr = state.tr.replaceWith(replaceFrom, replaceTo, state.schema.text(codeText, [mark]));
        dispatch(tr.scrollIntoView());
        return;
      }
    }

    // Scenario B: Pasted text itself is enclosed in single backticks: `hello world`
    if (trimmed.startsWith('`') && trimmed.endsWith('`') && !trimmed.startsWith('```') && trimmed.length >= 2 && !trimmed.includes('\n')) {
      e.preventDefault();
      e.stopPropagation();
      e.stopImmediatePropagation();
      const codeText = trimmed.slice(1, -1);
      const mark = codeMarkType.create();
      const tr = state.tr.replaceWith(from, to, state.schema.text(codeText, [mark]));
      dispatch(tr.scrollIntoView());
      return;
    }
  }

  // Run autoFormat after microtask if pasting generic text
  setTimeout(() => {
    const currentView = getEditorView();
    if (currentView) autoFormatInlineCode(currentView);
    resolveAllImagesInDOM();
  }, 10);
}

window.addEventListener('paste', handlePaste, true);
document.addEventListener('paste', handlePaste, true);

// Trigger autoFormatInlineCode on input and keyup
window.addEventListener('input', () => {
  const view = getEditorView();
  if (view) autoFormatInlineCode(view);
}, true);

window.addEventListener('keyup', (e) => {
  if (e.key === ' ' || e.key === 'Enter' || e.key === '`' || e.key === 'ArrowRight' || e.key === 'ArrowLeft') {
    const view = getEditorView();
    if (view) autoFormatInlineCode(view);
  }
}, true);

window.showLinkDialog = showLinkDialog;
window.applyLink = applyLink;
window.removeLinkAtSelection = removeLinkAtSelection;
window.resolveAllImagesInDOM = resolveAllImagesInDOM;
window.getEditorView = getEditorView;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', init);
} else {
  init();
}
