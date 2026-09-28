import { Crepe } from '@milkdown/crepe';
import { replaceAll } from '@milkdown/kit/utils';
import { editorViewCtx, commandsCtx } from '@milkdown/kit/core';
import { createTable, strikethroughSchema } from '@milkdown/kit/preset/gfm';
import { clearTextInCurrentBlockCommand, addBlockTypeCommand, selectTextNearPosCommand, codeBlockSchema, hrSchema, setBlockTypeCommand, linkSchema, strongSchema, emphasisSchema, inlineCodeSchema } from '@milkdown/kit/preset/commonmark';
import { Selection } from '@milkdown/kit/prose/state';
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

// Keyboard shortcut forwarding & browser hotkey prevention
window.addEventListener('keydown', (e) => {
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
    await crepeInstance.create();
    crepeInstance.editor.action((ctx) => {
      editorView = ctx.get(editorViewCtx);
    });
  } catch (err) {
    console.error('Failed to create Crepe editor:', err);
  }

  // Setup right-click & two-finger tap context formatting menu
  setupContextMenu();

  // Setup inline link insert & edit dialog
  setupLinkDialog();

  // Setup Obsidian & Word style heading folding
  // setupHeadingFolding();

  // Global methods accessible by host C# InvokeScript
  window.setMarkdown = (markdown) => {
    hasReceivedInitialContent = true;
    if (!crepeInstance) return;
    const current = crepeInstance.getMarkdown();
    if ((current || '').trim() === (markdown || '').trim()) {
      return;
    }
    isUpdatingFromHost = true;
    try {
      crepeInstance.editor.action(replaceAll(markdown || ''));
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

  // If C# host invoked setMarkdown before Crepe finished creating, apply it now
  if (window.__pendingMarkdown !== undefined) {
    const pending = window.__pendingMarkdown;
    delete window.__pendingMarkdown;
    window.setMarkdown(pending);
  }

  // Signal C# host that editor is loaded and ready
  sendMessage({ type: 'ready' });
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
  }
}

function toggleMark(markName, schemaHelper) {
  if (!crepeInstance) return;
  const view = crepeInstance.editor.ctx.get(editorViewCtx);
  if (!view) return;
  const { state, dispatch } = view;
  const { from, to, empty } = state.selection;
  if (empty) return;

  let markType = state.schema.marks[markName];
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

// Turn selected text into a link when a URL is pasted (Notion / Obsidian style)
function handlePasteLink(e) {
  const view = getEditorView();
  if (!view || !view.state) return;
  const { state, dispatch } = view;
  const { from, to, empty } = getEffectiveSelection(view);
  if (empty) return;

  const text = e.clipboardData ? e.clipboardData.getData('text/plain') : '';
  if (!text) return;

  const trimmed = text.trim();
  const isUrl = /^https?:\/\/\S+$/i.test(trimmed) || /^www\.\S+\.\S+$/i.test(trimmed);
  if (!isUrl) return;

  let cleanUrl = trimmed;
  if (!cleanUrl.includes('://') && !cleanUrl.startsWith('mailto:') && !cleanUrl.startsWith('#')) {
    cleanUrl = 'https://' + cleanUrl;
  }

  const linkType = state.schema.marks.link;
  if (!linkType) return;

  e.preventDefault();
  e.stopPropagation();

  const mark = linkType.create({ href: cleanUrl });
  const selectedText = state.doc.textBetween(from, to);
  const tr = state.tr.replaceWith(from, to, state.schema.text(selectedText, [mark]));
  dispatch(tr.scrollIntoView());
}

window.addEventListener('paste', handlePasteLink, true);
document.addEventListener('paste', handlePasteLink, true);

window.showLinkDialog = showLinkDialog;
window.applyLink = applyLink;
window.removeLinkAtSelection = removeLinkAtSelection;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', init);
} else {
  init();
}
