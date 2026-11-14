import { Crepe } from '@milkdown/crepe';
import { replaceAll, $remark, $prose } from '@milkdown/kit/utils';
import { visit } from 'unist-util-visit';
import { editorViewCtx, commandsCtx, parserCtx, schemaCtx } from '@milkdown/kit/core';
import { DOMParser, DOMSerializer } from '@milkdown/kit/prose/model';

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
    },
    handleKeyDown(view, event) {
      if (!view || !view.state) return false;
      const { state, dispatch } = view;
      const { selection } = state;
      const isImgNode = selection && selection.node && (selection.node.type.name === 'image-block' || selection.node.type.name === 'image');

      if (event.key === 'Enter' && isImgNode) {
        const pType = state.schema.nodes.paragraph;
        if (pType) {
          event.preventDefault();
          const afterPos = selection.to;
          let tr = state.tr;
          const nodeAfter = selection.$to.nodeAfter;
          if (!nodeAfter) {
            tr = tr.insert(afterPos, pType.create());
          }
          tr = tr.setSelection(Selection.near(tr.doc.resolve(afterPos + 1))).scrollIntoView();
          dispatch(tr);
          view.focus();
          return true;
        }
      }

      if (event.key === 'ArrowDown' && isImgNode) {
        const afterPos = selection.to;
        if (afterPos >= state.doc.content.size) {
          const pType = state.schema.nodes.paragraph;
          if (pType) {
            event.preventDefault();
            let tr = state.tr.insert(afterPos, pType.create());
            tr = tr.setSelection(Selection.near(tr.doc.resolve(afterPos + 1))).scrollIntoView();
            dispatch(tr);
            view.focus();
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

    let tr = null;

    const codeMarkType = newState.schema.marks.inlineCode || newState.schema.marks.code_inline;
    if (codeMarkType) {
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

      if (matches.length > 0) {
        tr = newState.tr;
        for (let i = matches.length - 1; i >= 0; i--) {
          const m = matches[i];
          const mark = codeMarkType.create();
          tr.replaceWith(m.from, m.to, newState.schema.text(m.code, [mark]));
        }
      }
    }

    // Ensure document does not end on a non-textblock (image-block, image, table, hr)
    // so user can always place cursor, paste, and type below it
    const currentDoc = tr ? tr.doc : newState.doc;
    const lastChild = currentDoc.lastChild;
    if (lastChild && (lastChild.type.name === 'image-block' || lastChild.type.name === 'image' || lastChild.type.name === 'table' || lastChild.type.name === 'hr' || !lastChild.isTextblock)) {
      const pType = newState.schema.nodes.paragraph;
      if (pType) {
        if (!tr) tr = newState.tr;
        tr = tr.insert(currentDoc.content.size, pType.create());
      }
    }

    // Do not carry inline code mark onto a brand new empty paragraph created via Enter
    const $selFrom = newState.selection.$from;
    const $oldSelFrom = oldState.selection.$from;
    if (newState.selection.empty &&
        $selFrom.parent &&
        $selFrom.parent.type.name === 'paragraph' &&
        $selFrom.parent.content.size === 0 &&
        $oldSelFrom.parent !== $selFrom.parent) {
      const activeMarks = newState.storedMarks || $selFrom.marks();
      if (activeMarks && activeMarks.some(m => m.type.name === 'code_inline' || m.type.name === 'inlineCode')) {
        if (!tr) tr = newState.tr;
        tr = tr.setStoredMarks(activeMarks.filter(m => m.type.name !== 'code_inline' && m.type.name !== 'inlineCode'));
      }
    }

    return tr && (tr.docChanged || tr.storedMarksSet) ? tr : null;
  }
}));

import { Decoration, DecorationSet } from '@milkdown/kit/prose/view';

const headingFoldPluginKey = new PluginKey('headingFoldPlugin');

function unfoldHeadingCoveringPos(view, targetPos) {
  if (!view || !view.state || typeof targetPos !== 'number') return;
  const pluginState = headingFoldPluginKey ? headingFoldPluginKey.getState(view.state) : null;
  if (!pluginState || !pluginState.folded || pluginState.folded.size === 0) return;

  const headings = [];
  view.state.doc.descendants((node, pos) => {
    if (node.type.name === 'heading') {
      headings.push({ pos, level: node.attrs.level || 1, node, size: node.nodeSize });
    }
  });

  const toUnfold = [];
  headings.forEach((h, idx) => {
    if (!pluginState.folded.has(h.pos)) return;

    let foldEnd = view.state.doc.content.size;
    for (let j = idx + 1; j < headings.length; j++) {
      if (headings[j].level <= h.level) {
        foldEnd = headings[j].pos;
        break;
      }
    }

    if (targetPos >= h.pos && targetPos <= foldEnd) {
      toUnfold.push(h.pos);
    }
  });

  if (toUnfold.length > 0) {
    let tr = view.state.tr;
    toUnfold.forEach(pos => {
      tr = tr.setMeta(headingFoldPluginKey, { unfoldPos: pos });
    });
    view.dispatch(tr);
  }
}

const headingFoldPlugin = $prose(() => {
  return new Plugin({
    key: headingFoldPluginKey,
    state: {
      init() {
        return { folded: new Set() };
      },
      apply(tr, value, oldState, newState) {
        const meta = tr.getMeta(headingFoldPluginKey);
        if (meta) {
          if (meta.togglePos !== undefined) {
            const newFolded = new Set(value.folded);
            if (newFolded.has(meta.togglePos)) {
              newFolded.delete(meta.togglePos);
            } else {
              newFolded.add(meta.togglePos);
            }
            return { folded: newFolded };
          }
          if (meta.unfoldPos !== undefined) {
            if (value.folded.has(meta.unfoldPos)) {
              const newFolded = new Set(value.folded);
              newFolded.delete(meta.unfoldPos);
              return { folded: newFolded };
            }
            return value;
          }
          if (meta.unfoldAll) {
            return { folded: new Set() };
          }
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

        const hiddenBlockPositions = new Set();

        headings.forEach((h, idx) => {
          if (!h.node || h.node.textContent.trim().length === 0) return;

          const isFolded = folded.has(h.pos);
          const btn = document.createElement('span');
          btn.className = 'heading-fold-btn' + (isFolded ? ' is-folded' : '');
          btn.contentEditable = 'false';
          btn.innerHTML = '<svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><path d="M7.41 8.59L12 13.17l4.59-4.58L18 10l-6 6-6-6 1.41-1.41z"/></svg>';
          btn.title = isFolded ? 'Click to expand section' : 'Click to collapse section';
          btn.dataset.headingPos = String(h.pos);

          let foldEnd = state.doc.content.size;
          for (let j = idx + 1; j < headings.length; j++) {
            if (headings[j].level <= h.level) {
              foldEnd = headings[j].pos;
              break;
            }
          }

          // If this heading is the last heading, do not fold trailing empty paragraph
          if (foldEnd === state.doc.content.size) {
            const lastChild = state.doc.lastChild;
            if (lastChild && lastChild.type.name === 'paragraph' && lastChild.content.size === 0) {
              const lastChildPos = state.doc.content.size - lastChild.nodeSize;
              if (lastChildPos > h.pos + h.size) {
                foldEnd = lastChildPos;
              }
            }
          }

          let lastToggleTime = 0;
          const toggleAction = (e) => {
            e.preventDefault();
            e.stopPropagation();
            const now = Date.now();
            if (now - lastToggleTime < 250) return;
            lastToggleTime = now;
            const view = getEditorView();
            if (view) {
              let tr = view.state.tr.setMeta(headingFoldPluginKey, { togglePos: h.pos });
              if (!isFolded) {
                // If folding, and selection is inside the section to be hidden, move selection to the heading
                const sel = view.state.selection;
                if (sel.from >= h.pos + h.size && sel.to <= foldEnd) {
                  const targetPos = Math.min(view.state.doc.content.size, h.pos + 1);
                  tr = tr.setSelection(Selection.near(view.state.doc.resolve(targetPos)));
                }
              }
              view.dispatch(tr);
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

            const foldStart = h.pos + h.size;
            if (foldEnd > foldStart) {
              state.doc.forEach((childNode, childOffset) => {
                if (childOffset >= foldStart && childOffset < foldEnd) {
                  if (!hiddenBlockPositions.has(childOffset)) {
                    hiddenBlockPositions.add(childOffset);
                    decos.push(Decoration.node(childOffset, childOffset + childNode.nodeSize, { class: 'heading-folded-hidden' }));
                  }
                }
              });
            }
          }
        });

        return DecorationSet.create(state.doc, decos);
      }
    }
  });
});

import { createTable, strikethroughSchema } from '@milkdown/kit/preset/gfm';
import { clearTextInCurrentBlockCommand, addBlockTypeCommand, selectTextNearPosCommand, codeBlockSchema, hrSchema, setBlockTypeCommand, linkSchema, strongSchema, emphasisSchema, inlineCodeSchema } from '@milkdown/kit/preset/commonmark';
import { Selection, TextSelection, NodeSelection, AllSelection, Plugin, PluginKey } from '@milkdown/kit/prose/state';
import { liftListItem } from '@milkdown/kit/prose/schema-list';
import { lift } from '@milkdown/kit/prose/commands';
import { liftTarget } from '@milkdown/kit/prose/transform';
import { isInTable, goToNextCell, addRowAfter } from '@milkdown/kit/prose/tables';
import nspell from 'nspell';
import aff from '../node_modules/dictionary-en/index.aff';
import dic from '../node_modules/dictionary-en/index.dic';
import '@milkdown/crepe/theme/common/style.css';
import '@milkdown/crepe/theme/frame-dark.css';

let speller = null;
try {
  speller = nspell(aff, dic);
} catch (e) {
  console.error('[Speller initialization failed]', e);
}

const ignoredWordsSet = new Set();
let isSpellCheckActive = true;
const spellCheckPluginKey = new PluginKey('spellCheckPlugin');

function buildSpellCheckDecorations(doc) {
  if (!isSpellCheckActive || !speller || !doc) return DecorationSet.empty;
  const decos = [];

  try {
    doc.descendants((node, pos, parent) => {
      if (parent && (parent.type.name.includes('code') || parent.type.name.includes('math'))) {
        return false;
      }
      if (!node.isText) return true;
      if (node.marks && node.marks.some(m => m.type.name.includes('code'))) {
        return;
      }
      const text = node.text;
      if (!text || text.length < 2) return;

      const wordRegex = /\b[A-Za-z]+(?:'[A-Za-z]+)?\b/g;
      let match;
      while ((match = wordRegex.exec(text)) !== null) {
        const rawWord = match[0];
        if (rawWord.length >= 2 && isWordMisspelled(rawWord)) {
          const from = pos + match.index;
          const to = from + rawWord.length;
          decos.push(Decoration.inline(from, to, {
            class: 'misspelled-word',
            'data-word': rawWord
          }));
        }
      }
    });
  } catch (err) {
    console.error('[SpellCheck] buildDecorations error:', err);
  }

  return DecorationSet.create(doc, decos);
}

const spellCheckPlugin = $prose(() => new Plugin({
  key: spellCheckPluginKey,
  state: {
    init(config, instance) {
      return buildSpellCheckDecorations(instance.doc);
    },
    apply(tr, oldDecoSet, oldState, newState) {
      if (!isSpellCheckActive || !speller) {
        return DecorationSet.empty;
      }
      const meta = tr.getMeta(spellCheckPluginKey);
      if (meta || tr.docChanged) {
        return buildSpellCheckDecorations(newState.doc);
      }
      return oldDecoSet.map(tr.mapping, newState.doc);
    }
  },
  props: {
    decorations(state) {
      return this.getState(state);
    },
    handleClick(view, pos, event) {
      const target = event.target;
      if (target && target.classList && target.classList.contains('misspelled-word')) {
        const wordInfo = getWordAtPos(view.state.doc, pos);
        if (wordInfo && isWordMisspelled(wordInfo.word)) {
          try {
            const coords = view.coordsAtPos(wordInfo.from);
            showSpellPopup(wordInfo, coords);
            return true;
          } catch (err) {}
        }
      }
      return false;
    }
  }
}));

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

function handleTableTab(view, shiftKey) {
  if (!view || !view.state) return false;
  const { state, dispatch } = view;
  if (!isInTable(state)) return false;

  if (shiftKey) {
    return goToNextCell(-1)(state, dispatch);
  }

  if (goToNextCell(1)(state, dispatch)) {
    return true;
  }

  // Last cell of table reached! Add a new row to the table:
  if (addRowAfter(state, dispatch)) {
    // Focus into the first cell of the newly added row:
    goToNextCell(1)(view.state, view.dispatch);
    return true;
  }

  return false;
}

// Keyboard shortcut forwarding & browser hotkey prevention
window.addEventListener('keydown', (e) => {
  if (e.key === 'Tab' && !e.ctrlKey && !e.altKey && !e.metaKey) {
    const view = getEditorView();
    if (view && handleTableTab(view, e.shiftKey)) {
      e.preventDefault();
      e.stopPropagation();
      e.stopImmediatePropagation();
      return;
    }
  }

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
      if (typeof window.flushContentChanged === 'function') {
        window.flushContentChanged();
      }
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
      e.stopPropagation();
      if (document.activeElement) document.activeElement.blur();
      window.blur();
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
    if (e.key.toLowerCase() === 'f') {
      e.preventDefault();
      e.stopPropagation();
      if (window.openFindWidget) {
        window.openFindWidget();
      }
      return;
    }
    if (e.key.toLowerCase() === 'n') {
      e.preventDefault();
      sendMessage({ type: 'shortcut', action: 'newFolder' });
      return;
    }
    if (e.key.toLowerCase() === 'v') {
      lastPlainPasteTimestamp = Date.now();
    }
  } else if (e.key === 'F5') {
    e.preventDefault();
  } else if (e.key === 'Escape') {
    if (window.isFindWidgetOpen && window.isFindWidgetOpen()) {
      e.preventDefault();
      window.closeFindWidget();
      return;
    }
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

  let contentChangedDebounceTimer = null;
  let pendingMarkdownToSend = null;

  function flushContentChanged() {
    if (contentChangedDebounceTimer) {
      clearTimeout(contentChangedDebounceTimer);
      contentChangedDebounceTimer = null;
    }
    if (pendingMarkdownToSend !== null) {
      const md = pendingMarkdownToSend;
      pendingMarkdownToSend = null;
      sendMessage({
        type: 'contentChanged',
        markdown: md,
      });
    }
  }

  window.flushContentChanged = flushContentChanged;

  let lastMarkdownFromHost = null;

  // Listen to markdown changes and send back to host (150ms debounce for production standard typing performance)
  crepeInstance.on((listener) => {
    listener.markdownUpdated((ctx, markdown, prevMarkdown) => {
      if (isUpdatingFromHost) return;
      if (lastMarkdownFromHost !== null) {
        if ((markdown || '').trim() === lastMarkdownFromHost.trim()) {
          return;
        }
        lastMarkdownFromHost = null;
      }
      if (!hasReceivedInitialContent && !markdown) return;
      pendingMarkdownToSend = markdown;
      if (contentChangedDebounceTimer) {
        clearTimeout(contentChangedDebounceTimer);
      }
      contentChangedDebounceTimer = setTimeout(() => {
        contentChangedDebounceTimer = null;
        if (pendingMarkdownToSend !== null) {
          const md = pendingMarkdownToSend;
          pendingMarkdownToSend = null;
          sendMessage({
            type: 'contentChanged',
            markdown: md,
          });
        }
      }, 150);
    });
  });

  window.addEventListener('blur', () => {
    flushContentChanged();
  });

  try {
    crepeInstance.editor.use(remarkFixImageNodes);
    crepeInstance.editor.use(autoInlineCodePlugin);
    crepeInstance.editor.use(headingFoldPlugin);
    crepeInstance.editor.use(spellCheckPlugin);
    await crepeInstance.create();
    crepeInstance.editor.action((ctx) => {
      editorView = ctx.get(editorViewCtx);
    });
    applySpellCheck(true);
  } catch (err) {
    console.error('Failed to create Crepe editor:', err);
  }

  // Setup right-click & two-finger tap context formatting menu
  setupContextMenu();

  // Setup inline link insert & edit dialog
  setupLinkDialog();

  // Global methods accessible by host C# InvokeScript
  window.setMarkdown = (markdown, force) => {
    if (contentChangedDebounceTimer) {
      clearTimeout(contentChangedDebounceTimer);
      contentChangedDebounceTimer = null;
    }
    pendingMarkdownToSend = null;
    hasReceivedInitialContent = true;
    if (!crepeInstance) return;
    const current = crepeInstance.getMarkdown();
    if ((current || '').trim() === (markdown || '').trim()) {
      return;
    }

    // Protection: If user is actively typing/focused inside editor,
    // NEVER clobber their in-progress typing, caret, or newlines with delayed host state!
    // However, if force is true (e.g. switching notes or creating a new note), we MUST update.
    if (!force) {
      const activeEl = document.activeElement;
      const isEditorActive = document.hasFocus() && activeEl && (activeEl.closest('.ProseMirror') || activeEl.closest('.milkdown'));
      if (isEditorActive) {
        return;
      }
    }

    const cleanMd = (markdown || '').replace(/(!\[.*?\]\([^\n\r]*?\))\r?\n(?=!\[)/g, '$1\n\n');
    lastMarkdownFromHost = cleanMd;

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
      crepeInstance.editor.action((ctx) => {
        const view = ctx.get(editorViewCtx);
        if (view && view.state) {
          const doc = view.state.doc;
          const lastChild = doc.lastChild;
          if (lastChild && (lastChild.type.name === 'image-block' || lastChild.type.name === 'image' || lastChild.type.name === 'table' || lastChild.type.name === 'hr' || !lastChild.isTextblock)) {
            const pType = view.state.schema.nodes.paragraph;
            if (pType) {
              view.dispatch(view.state.tr.insert(doc.content.size, pType.create()));
            }
          }
          if (force) {
            try {
              const startSel = Selection.atStart(view.state.doc);
              view.dispatch(view.state.tr.setSelection(startSel).setMeta(headingFoldPluginKey, { unfoldAll: true }).scrollIntoView());
            } catch (_) {}
            const scrollContainer = document.querySelector('.milkdown') || document.documentElement;
            if (scrollContainer) scrollContainer.scrollTop = 0;
          }
        }
      });
      setTimeout(resolveAllImagesInDOM, 60);
      setTimeout(() => {
        if (window.updateHeadingFolds) window.updateHeadingFolds();
        if (window.refreshFindWidget) window.refreshFindWidget();
        if (isSpellCheckActive) applySpellCheck(true);
      }, 30);
    } catch (e) {
      console.error('[setMarkdown] Failed to replace markdown:', e);
    } finally {
      setTimeout(() => {
        isUpdatingFromHost = false;
      }, 300);
    }
  };

  window.getMarkdown = () => {
    flushContentChanged();
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

  // Interactive visual feedback for Code Block copy button
  document.addEventListener('click', (e) => {
    const btn = e.target.closest('.copy-button');
    if (btn) {
      if (btn.dataset.copying) return;
      btn.dataset.copying = 'true';
      const originalHTML = btn.innerHTML;
      btn.classList.add('copied');
      btn.innerHTML = '<span class="milkdown-icon" style="color:#10b981;font-weight:bold;margin-right:4px;">✓</span><span style="color:#10b981;font-weight:600;">Copied!</span>';
      setTimeout(() => {
        btn.innerHTML = originalHTML;
        btn.classList.remove('copied');
        delete btn.dataset.copying;
      }, 2000);
    }
  }, true);

  // ── VS Code Find Widget Implementation ───────────────────────────
  const findWidgetEl = document.getElementById('vscode-find-widget');
  const findInputEl = document.getElementById('find-input');
  const findReplaceInputEl = document.getElementById('find-replace-input');
  const findCountEl = document.getElementById('find-count');
  const findExpandBtn = document.getElementById('find-toggle-replace');
  const findReplacePart = document.getElementById('find-replace-part');
  const findCaseToggle = document.getElementById('find-case-toggle');
  const findWordToggle = document.getElementById('find-word-toggle');
  const findRegexToggle = document.getElementById('find-regex-toggle');
  const findPrevBtn = document.getElementById('find-prev-btn');
  const findNextBtn = document.getElementById('find-next-btn');
  const findCloseBtn = document.getElementById('find-close-btn');
  const findReplaceBtn = document.getElementById('find-replace-btn');
  const findReplaceAllBtn = document.getElementById('find-replace-all-btn');

  const findState = {
    isOpen: false,
    query: '',
    matchCase: false,
    wholeWord: false,
    useRegex: false,
    matches: [],
    currentIndex: -1
  };

  function collectSearchableText(rootEl) {
    if (!rootEl) return { fullText: '', charMap: [] };
    const walker = document.createTreeWalker(
      rootEl,
      NodeFilter.SHOW_TEXT,
      {
        acceptNode(node) {
          if (!node.textContent || node.textContent.length === 0) return NodeFilter.FILTER_REJECT;
          const parent = node.parentElement;
          if (!parent) return NodeFilter.FILTER_REJECT;
          if (parent.closest('#vscode-find-widget, #format-menu, #spell-popup, #link-dialog')) {
            return NodeFilter.FILTER_REJECT;
          }
          const tag = parent.tagName.toLowerCase();
          if (tag === 'script' || tag === 'style') return NodeFilter.FILTER_REJECT;
          return NodeFilter.FILTER_ACCEPT;
        }
      }
    );

    let fullText = '';
    const charMap = [];
    let prevBlock = null;
    let textNode;

    while ((textNode = walker.nextNode())) {
      const parent = textNode.parentElement;
      const block = parent ? parent.closest('.cm-line, p, h1, h2, h3, h4, h5, h6, li, tr, blockquote, pre') : null;
      if (prevBlock && block && block !== prevBlock) {
        fullText += '\n';
        charMap.push(null);
      }
      prevBlock = block;

      const str = textNode.textContent;
      for (let i = 0; i < str.length; i++) {
        fullText += str[i];
        charMap.push({ node: textNode, offset: i });
      }
    }

    return { fullText, charMap };
  }

  function clearHighlights() {
    if (typeof CSS !== 'undefined' && CSS.highlights) {
      try {
        CSS.highlights.delete('vscode-find-match');
        CSS.highlights.delete('vscode-find-match-current');
      } catch (e) {}
    }
  }

  function renderHighlights() {
    if (typeof CSS !== 'undefined' && CSS.highlights) {
      try {
        if (findState.matches.length === 0) {
          CSS.highlights.delete('vscode-find-match');
          CSS.highlights.delete('vscode-find-match-current');
          return;
        }

        const allRanges = [];
        let currentRange = null;

        findState.matches.forEach((m, idx) => {
          if (idx === findState.currentIndex) {
            currentRange = m.range;
          } else {
            allRanges.push(m.range);
          }
        });

        if (allRanges.length > 0) {
          CSS.highlights.set('vscode-find-match', new Highlight(...allRanges));
        } else {
          CSS.highlights.delete('vscode-find-match');
        }

        if (currentRange) {
          CSS.highlights.set('vscode-find-match-current', new Highlight(currentRange));
        } else {
          CSS.highlights.delete('vscode-find-match-current');
        }
      } catch (e) {
        console.warn('[FindWidget] renderHighlights error:', e);
      }
    }
  }

  function updateFindCountDisplay(isError = false) {
    if (!findCountEl) return;
    if (!findState.query) {
      findCountEl.textContent = '';
      findCountEl.className = 'find-count';
      if (findPrevBtn) findPrevBtn.disabled = true;
      if (findNextBtn) findNextBtn.disabled = true;
      return;
    }

    if (isError || findState.matches.length === 0) {
      findCountEl.textContent = 'No results';
      findCountEl.className = 'find-count no-results';
      if (findPrevBtn) findPrevBtn.disabled = true;
      if (findNextBtn) findNextBtn.disabled = true;
    } else {
      findCountEl.textContent = `${findState.currentIndex + 1} of ${findState.matches.length}`;
      findCountEl.className = 'find-count';
      if (findPrevBtn) findPrevBtn.disabled = false;
      if (findNextBtn) findNextBtn.disabled = false;
    }
  }

  function scrollToCurrentMatch() {
    if (findState.currentIndex < 0 || findState.currentIndex >= findState.matches.length) return;
    const current = findState.matches[findState.currentIndex];
    if (!current || !current.range) return;

    try {
      const view = getEditorView();
      if (view) {
        try {
          const matchPos = view.posAtDOM(current.range.startContainer, current.range.startOffset);
          if (typeof matchPos === 'number') {
            unfoldHeadingCoveringPos(view, matchPos);
          }
        } catch (err) {}
      }

      setTimeout(() => {
        const el = current.range.startContainer.nodeType === Node.ELEMENT_NODE
          ? current.range.startContainer
          : current.range.startContainer.parentElement;

        if (el && el.scrollIntoView) {
          el.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'nearest' });
        }
        renderHighlights();
      }, 30);
    } catch (e) {
      console.warn('[FindWidget] scroll error:', e);
    }
  }

  function performFind(query, keepIndex = false) {
    findState.query = query || '';
    if (!query) {
      findState.matches = [];
      findState.currentIndex = -1;
      clearHighlights();
      updateFindCountDisplay();
      return;
    }

    const editorEl = document.getElementById('editor');
    const { fullText, charMap } = collectSearchableText(editorEl);
    if (!fullText) {
      findState.matches = [];
      findState.currentIndex = -1;
      clearHighlights();
      updateFindCountDisplay();
      return;
    }

    let regex = null;
    try {
      let pattern;
      if (findState.useRegex) {
        pattern = query;
      } else {
        pattern = query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        if (findState.wholeWord) {
          pattern = `\\b${pattern}\\b`;
        }
      }
      const flags = findState.matchCase ? 'g' : 'gi';
      regex = new RegExp(pattern, flags);
    } catch (e) {
      findState.matches = [];
      findState.currentIndex = -1;
      clearHighlights();
      updateFindCountDisplay(true);
      return;
    }

    const matches = [];
    let match;
    while ((match = regex.exec(fullText)) !== null) {
      if (match[0].length === 0) {
        regex.lastIndex++;
        continue;
      }
      const sIdx = match.index;
      const eIdx = sIdx + match[0].length;
      const startMapping = charMap[sIdx];
      const endMapping = charMap[eIdx - 1];

      if (startMapping && endMapping) {
        try {
          const range = document.createRange();
          range.setStart(startMapping.node, startMapping.offset);
          range.setEnd(endMapping.node, endMapping.offset + 1);
          matches.push({
            range,
            text: match[0]
          });
        } catch (err) {
          console.warn('[FindWidget] Range error:', err);
        }
      }
    }

    findState.matches = matches;

    if (matches.length === 0) {
      findState.currentIndex = -1;
      clearHighlights();
      updateFindCountDisplay();
      return;
    }

    if (keepIndex && findState.currentIndex >= 0 && findState.currentIndex < matches.length) {
      // keep current index
    } else {
      findState.currentIndex = 0;
    }

    updateFindCountDisplay();
    renderHighlights();
    scrollToCurrentMatch();
  }

  function findNext() {
    if (findState.matches.length === 0) return;
    findState.currentIndex = (findState.currentIndex + 1) % findState.matches.length;
    updateFindCountDisplay();
    renderHighlights();
    scrollToCurrentMatch();
  }

  function findPrev() {
    if (findState.matches.length === 0) return;
    findState.currentIndex = (findState.currentIndex - 1 + findState.matches.length) % findState.matches.length;
    updateFindCountDisplay();
    renderHighlights();
    scrollToCurrentMatch();
  }

  function getEditorSelectedText() {
    const sel = window.getSelection();
    if (sel && !sel.isCollapsed) {
      const text = sel.toString().trim();
      if (text && !text.includes('\n')) {
        return text;
      }
    }
    return '';
  }

  function openFindWidget(initialQuery) {
    if (!findWidgetEl) return;
    findWidgetEl.style.display = 'flex';
    findState.isOpen = true;

    if (initialQuery !== undefined && initialQuery !== null && initialQuery !== '') {
      if (findInputEl) findInputEl.value = initialQuery;
    } else {
      const selected = getEditorSelectedText();
      if (selected && findInputEl) {
        findInputEl.value = selected;
      }
    }

    if (findInputEl) {
      findInputEl.focus();
      findInputEl.select();
      performFind(findInputEl.value);
    }
  }

  function closeFindWidget() {
    if (!findWidgetEl) return;
    findWidgetEl.style.display = 'none';
    findState.isOpen = false;
    clearHighlights();

    const pm = document.querySelector('.milkdown .ProseMirror');
    if (pm) pm.focus();
  }

  function toggleReplaceRow() {
    if (!findReplacePart || !findExpandBtn) return;
    const isVisible = findReplacePart.style.display !== 'none';
    if (isVisible) {
      findReplacePart.style.display = 'none';
      findExpandBtn.classList.remove('expanded');
    } else {
      findReplacePart.style.display = 'flex';
      findExpandBtn.classList.add('expanded');
      if (findReplaceInputEl) findReplaceInputEl.focus();
    }
  }

  function getMatchDocRange(view, range) {
    if (!view || !view.state || !range) return null;
    try {
      const from = view.posAtDOM(range.startContainer, range.startOffset);
      const to = view.posAtDOM(range.endContainer, range.endOffset);
      if (typeof from === 'number' && typeof to === 'number' && from <= to) {
        return { from, to };
      }
    } catch (e) {}
    return null;
  }

  function doFindReplaceCurrent() {
    if (findState.matches.length === 0 || findState.currentIndex < 0) return;
    const match = findState.matches[findState.currentIndex];
    const rep = (findReplaceInputEl && findReplaceInputEl.value) || '';
    const view = getEditorView();

    let handled = false;
    if (view && view.state) {
      const pmRange = getMatchDocRange(view, match.range);
      if (pmRange) {
        unfoldHeadingCoveringPos(view, pmRange.from);
        const tr = view.state.tr.insertText(rep, pmRange.from, pmRange.to);
        view.dispatch(tr);
        handled = true;
      }
    }

    if (!handled) {
      try {
        const sel = window.getSelection();
        if (sel) {
          sel.removeAllRanges();
          sel.addRange(match.range);
          document.execCommand('insertText', false, rep);
        }
      } catch (e) {
        console.error('[FindWidget] Replace failed:', e);
      }
    }

    setTimeout(() => {
      performFind((findInputEl && findInputEl.value) || '', false);
    }, 20);
  }

  function doFindReplaceAll() {
    if (findState.matches.length === 0) return;
    const rep = (findReplaceInputEl && findReplaceInputEl.value) || '';
    const view = getEditorView();

    if (view && view.state) {
      const docRanges = [];
      const fallbackMatches = [];

      findState.matches.forEach(m => {
        const pmRange = getMatchDocRange(view, m.range);
        if (pmRange) {
          docRanges.push(pmRange);
        } else {
          fallbackMatches.push(m);
        }
      });

      if (docRanges.length > 0) {
        // Sort descending by position so text replacement does not shift earlier positions
        docRanges.sort((a, b) => b.from - a.from);

        let tr = view.state.tr;
        for (const r of docRanges) {
          unfoldHeadingCoveringPos(view, r.from);
          tr = tr.insertText(rep, r.from, r.to);
        }
        view.dispatch(tr);
      }

      if (fallbackMatches.length > 0) {
        const sel = window.getSelection();
        for (let i = fallbackMatches.length - 1; i >= 0; i--) {
          try {
            if (sel) {
              sel.removeAllRanges();
              sel.addRange(fallbackMatches[i].range);
              document.execCommand('insertText', false, rep);
            }
          } catch (e) {}
        }
      }
    }

    setTimeout(() => {
      performFind((findInputEl && findInputEl.value) || '', false);
    }, 20);
  }

  if (findInputEl) {
    findInputEl.addEventListener('input', () => {
      performFind(findInputEl.value);
    });

    findInputEl.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        if (e.shiftKey) {
          findPrev();
        } else {
          findNext();
        }
      } else if (e.key === 'Escape') {
        e.preventDefault();
        closeFindWidget();
      } else if (e.altKey && e.key.toLowerCase() === 'c') {
        e.preventDefault();
        if (findCaseToggle) findCaseToggle.click();
      } else if (e.altKey && e.key.toLowerCase() === 'w') {
        e.preventDefault();
        if (findWordToggle) findWordToggle.click();
      } else if (e.altKey && e.key.toLowerCase() === 'r') {
        e.preventDefault();
        if (findRegexToggle) findRegexToggle.click();
      }
    });
  }

  if (findReplaceInputEl) {
    findReplaceInputEl.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        if (e.ctrlKey && e.altKey) {
          doFindReplaceAll();
        } else {
          doFindReplaceCurrent();
        }
      } else if (e.key === 'Escape') {
        e.preventDefault();
        closeFindWidget();
      }
    });
  }

  if (findExpandBtn) {
    findExpandBtn.addEventListener('click', toggleReplaceRow);
  }

  if (findCaseToggle) {
    findCaseToggle.addEventListener('click', () => {
      findState.matchCase = !findState.matchCase;
      findCaseToggle.classList.toggle('active', findState.matchCase);
      if (findInputEl) performFind(findInputEl.value);
    });
  }

  if (findWordToggle) {
    findWordToggle.addEventListener('click', () => {
      findState.wholeWord = !findState.wholeWord;
      findWordToggle.classList.toggle('active', findState.wholeWord);
      if (findInputEl) performFind(findInputEl.value);
    });
  }

  if (findRegexToggle) {
    findRegexToggle.addEventListener('click', () => {
      findState.useRegex = !findState.useRegex;
      findRegexToggle.classList.toggle('active', findState.useRegex);
      if (findInputEl) performFind(findInputEl.value);
    });
  }

  if (findPrevBtn) findPrevBtn.addEventListener('click', findPrev);
  if (findNextBtn) findNextBtn.addEventListener('click', findNext);
  if (findCloseBtn) findCloseBtn.addEventListener('click', closeFindWidget);
  if (findReplaceBtn) findReplaceBtn.addEventListener('click', doFindReplaceCurrent);
  if (findReplaceAllBtn) findReplaceAllBtn.addEventListener('click', doFindReplaceAll);

  function focusOrInsertLeadingParagraph(view) {
    if (!view || !view.state) return;
    const { state, dispatch } = view;
    const firstChild = state.doc.firstChild;
    const hasLeadingEmptyP = firstChild && firstChild.type.name === 'paragraph' && firstChild.content.size === 0;

    let tr = state.tr;
    if (!hasLeadingEmptyP) {
      const pType = state.schema.nodes.paragraph;
      if (!pType) return;
      tr = tr.insert(0, pType.create());
    }
    const targetPos = Math.min(1, tr.doc.content.size);
    tr = tr.setSelection(Selection.near(tr.doc.resolve(targetPos))).scrollIntoView();
    dispatch(tr);
    view.focus();
  }

  const editorContainer = document.getElementById('editor-container');
  if (editorContainer) {
    editorContainer.addEventListener('input', () => {
      if (findState.isOpen && findInputEl && findInputEl.value) {
        performFind(findInputEl.value, true);
      }
    });

    let mouseDownPos = { x: 0, y: 0 };
    editorContainer.addEventListener('mousedown', (e) => {
      mouseDownPos = { x: e.clientX, y: e.clientY };
    }, true);

    // Allow ArrowUp / ArrowLeft at start of a leading code block to place cursor above it
    editorContainer.addEventListener('keydown', (e) => {
      if (e.ctrlKey || e.altKey || e.metaKey || e.shiftKey) return;
      if (e.key !== 'ArrowUp' && e.key !== 'ArrowLeft') return;
      const cmContent = e.target && e.target.closest ? e.target.closest('.cm-content') : null;
      if (!cmContent) return;

      const codeBlockEl = cmContent.closest('.milkdown-code-block');
      const pm = document.querySelector('.milkdown .ProseMirror');
      if (!codeBlockEl || !pm) return;

      const firstLine = cmContent.firstElementChild;
      const sel = window.getSelection();
      if (!firstLine || !sel || !sel.isCollapsed || !firstLine.contains(sel.anchorNode)) return;

      if (e.key === 'ArrowLeft') {
        const preRange = document.createRange();
        preRange.setStart(firstLine, 0);
        preRange.setEnd(sel.anchorNode, sel.anchorOffset);
        if (preRange.toString().length !== 0) return;
      }

      const view = getEditorView();
      if (!view || !view.state) return;

      const firstCodeBlock = pm.querySelector('.milkdown-code-block');
      const firstDocChild = view.state.doc.firstChild;
      if (firstCodeBlock === codeBlockEl && firstDocChild && firstDocChild.type.name === 'code_block') {
        e.preventDefault();
        e.stopPropagation();
        focusOrInsertLeadingParagraph(view);
      }
    }, true);

    editorContainer.addEventListener('click', (e) => {
      // If user was dragging to select, don't trigger click action
      const moveDist = Math.hypot(e.clientX - mouseDownPos.x, e.clientY - mouseDownPos.y);
      if (moveDist > 4) {
        return;
      }

      // Clicking in a code block's left gutter or left padding places cursor at column 0 of that line
      const clickedCodeBlock = e.target && e.target.closest ? e.target.closest('.milkdown-code-block') : null;
      if (clickedCodeBlock && !e.target.closest('.tools, button, input, .cm-content, [title*="Fold"], [title*="Unfold"]')) {
        const cmContent = clickedCodeBlock.querySelector('.cm-content');
        const lines = cmContent ? Array.from(cmContent.querySelectorAll('.cm-line')) : [];
        if (cmContent && lines.length > 0) {
          let targetLine = lines[0];
          let bestDist = Infinity;
          for (const line of lines) {
            const r = line.getBoundingClientRect();
            if (e.clientY >= r.top && e.clientY <= r.bottom) {
              targetLine = line;
              bestDist = 0;
              break;
            }
            const dist = Math.min(Math.abs(e.clientY - r.top), Math.abs(e.clientY - r.bottom));
            if (dist < bestDist) {
              bestDist = dist;
              targetLine = line;
            }
          }
          cmContent.focus();
          const sel = window.getSelection();
          if (sel) {
            const range = document.createRange();
            range.setStart(targetLine, 0);
            range.collapse(true);
            sel.removeAllRanges();
            sel.addRange(range);
          }
          return;
        }
      }

      // Don't trigger if clicked on controls, buttons, menus, inputs, tables, etc.
      if (e.target.closest('.heading-fold-btn, .heading-fold-badge, .format-menu, .bubble-menu, .slash-menu, .find-widget, .cm-editor, table, a, input, button, .tools')) {
        return;
      }

      // Never collapse an active text selection on click
      const domSel = window.getSelection();
      if (domSel && !domSel.isCollapsed && domSel.toString().trim().length > 0) {
        return;
      }

      const view = getEditorView();
      if (!view || !view.state) return;
      if (!view.state.selection.empty) return;

      const pm = document.querySelector('.milkdown .ProseMirror');
      if (!pm) return;

      // Find all visible non-virtual elements
      const visibleElements = Array.from(pm.children).filter(el => {
        return el.offsetParent !== null &&
          !el.classList.contains('heading-folded-hidden') &&
          !el.classList.contains('prosemirror-virtual-cursor') &&
          !el.classList.contains('ProseMirror-widget') &&
          el.getBoundingClientRect().height > 0;
      });

      const firstTop = visibleElements.length > 0
        ? visibleElements[0].getBoundingClientRect().top
        : pm.getBoundingClientRect().top;

      let lastBottom = 0;
      if (visibleElements.length > 0) {
        const lastEl = visibleElements[visibleElements.length - 1];
        lastBottom = lastEl.getBoundingClientRect().bottom;
      } else {
        lastBottom = pm.getBoundingClientRect().top;
      }

      const isContainerBg = (e.target === editorContainer || e.target === pm || e.target === pm.parentElement);

      // Clicking strictly ABOVE all document content on the top container padding places cursor before first block
      if (e.clientY <= firstTop + 2 && isContainerBg) {
        focusOrInsertLeadingParagraph(view);
        return;
      }

      // Only trigger if clicked strictly BELOW all document content on the empty container
      if (e.clientY > lastBottom + 5 && (e.target === editorContainer || e.target === pm.parentElement)) {
        const { state, dispatch } = view;
        const lastChild = state.doc.lastChild;
        const hasTrailingEmptyP = lastChild && lastChild.type.name === 'paragraph' && lastChild.content.size === 0;

        let tr = state.tr;
        if (!hasTrailingEmptyP) {
          const emptyPara = state.schema.nodes.paragraph.create();
          tr = tr.insert(state.doc.content.size, emptyPara);
        }

        const targetPos = Math.max(0, tr.doc.content.size - 1);
        tr = tr.setSelection(Selection.near(tr.doc.resolve(targetPos))).scrollIntoView();
        dispatch(tr);
        view.focus();
      }
    });
  }

  window.openFindWidget = openFindWidget;
  window.closeFindWidget = closeFindWidget;
  window.isFindWidgetOpen = () => findState.isOpen;
  window.refreshFindWidget = () => {
    if (findState.isOpen && findInputEl && findInputEl.value) {
      performFind(findInputEl.value, true);
    }
  };

  window.findInPage = (query, backwards) => {
    if (!query) {
      clearHighlights();
      return { count: 0, found: false };
    }
    openFindWidget(query);
    if (backwards) {
      findPrev();
    } else {
      findNext();
    }
    return { count: findState.matches.length, found: findState.matches.length > 0 };
  };

  window.clearFindHighlight = () => {
    closeFindWidget();
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
  isSpellCheckActive = !!enabled;
  const isEnabled = isSpellCheckActive;
  // Native browser spellcheck is always OFF: only our own red wavy decorations
  // (which respect the ignore list) are used.
  const pm = document.querySelector('.ProseMirror');
  if (pm) pm.setAttribute('spellcheck', 'false');
  const root = document.getElementById('editor');
  if (root) root.setAttribute('spellcheck', 'false');
  const container = document.getElementById('editor-container');
  if (container) container.setAttribute('spellcheck', 'false');
  document.body.setAttribute('spellcheck', 'false');

  const view = getEditorView();
  if (view && view.state) {
    view.dispatch(view.state.tr.setMeta(spellCheckPluginKey, { enabled: isEnabled }));
  }
}

let contextMenuX = 0;
let contextMenuY = 0;
let contextMenuCodeBlockEl = null;
let lastPlainPasteTimestamp = 0;

window.setIgnoredWords = (words) => {
  if (Array.isArray(words)) {
    ignoredWordsSet.clear();
    words.forEach((w) => {
      if (w && typeof w === 'string') {
        const clean = w.trim().toLowerCase();
        if (clean) ignoredWordsSet.add(clean);
      }
    });
    const view = getEditorView();
    if (view && view.state) {
      view.dispatch(view.state.tr.setMeta(spellCheckPluginKey, { refresh: true }));
    }
  }
};

function isWordMisspelled(word) {
  if (!word || !speller) return false;
  const clean = word.trim().replace(/^['"-]+|['"-]+$/g, '');
  if (!clean || clean.length < 2) return false;
  if (/\d/.test(clean)) return false;
  if (clean.includes('/') || clean.includes('\\') || clean.includes('_') || clean.includes(':') || clean.includes('@')) return false;

  const lower = clean.toLowerCase();
  if (ignoredWordsSet.has(lower)) return false;

  if (speller.correct(clean) || speller.correct(lower)) {
    return false;
  }

  return true;
}

function getSpellSuggestions(word) {
  if (!speller) return [];
  const clean = word.trim().replace(/^['"-]+|['"-]+$/g, '');
  const suggestions = speller.suggest(clean);
  return suggestions.slice(0, 4);
}

function getWordAtPos(doc, pos) {
  if (!doc || pos < 0 || pos > doc.content.size) return null;
  const $pos = doc.resolve(pos);
  if (!$pos.parent || !$pos.parent.isTextblock) return null;

  const text = $pos.parent.textContent;
  const offset = $pos.parentOffset;

  let start = offset;
  let end = offset;

  if ((offset === text.length || /\s|[^\w']/.test(text[offset])) && offset > 0 && !/\s|[^\w']/.test(text[offset - 1])) {
    start = offset - 1;
    end = offset;
  }

  while (start > 0 && !/\s|[^\w']/.test(text[start - 1])) {
    start--;
  }
  while (end < text.length && !/\s|[^\w']/.test(text[end])) {
    end++;
  }

  let rawWord = text.slice(start, end).replace(/^[']+|[']+$/g, '');
  if (!rawWord || rawWord.length < 2) return null;

  const from = $pos.start() + start;
  const to = $pos.start() + end;

  return { word: rawWord, from, to };
}

function replaceWord(from, to, replacement) {
  const view = getEditorView();
  if (!view || !view.state) return;
  const tr = view.state.tr.replaceWith(from, to, view.state.schema.text(replacement));
  const newPos = from + replacement.length;
  tr.setSelection(Selection.near(tr.doc.resolve(newPos))).scrollIntoView();
  view.dispatch(tr);
  view.focus();
}

let currentSpellWordInfo = null;
let spellPopupTimer = null;

function showSpellPopup(wordInfo, coords) {
  const popup = document.getElementById('spell-popup');
  if (!popup) return;

  const list = document.getElementById('spell-suggestions-list');
  if (!list) return;

  const suggestions = getSpellSuggestions(wordInfo.word);
  if (!suggestions || suggestions.length === 0) {
    hideSpellPopup();
    return;
  }

  currentSpellWordInfo = wordInfo;
  list.innerHTML = '';

  suggestions.forEach((sugg) => {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'spell-suggestion-item';
    btn.textContent = sugg;
    btn.addEventListener('pointerdown', (e) => e.preventDefault());
    btn.addEventListener('click', (e) => {
      e.preventDefault();
      e.stopPropagation();
      replaceWord(wordInfo.from, wordInfo.to, sugg);
      hideSpellPopup();
    });
    list.appendChild(btn);
  });

  const ignoreBtn = document.getElementById('spell-ignore-btn');
  if (ignoreBtn) {
    ignoreBtn.onclick = (e) => {
      e.preventDefault();
      e.stopPropagation();
      const lower = wordInfo.word.toLowerCase();
      ignoredWordsSet.add(lower);
      const view = getEditorView();
      if (view && view.state) {
        view.dispatch(view.state.tr.setMeta(spellCheckPluginKey, { refresh: true }));
      }
      sendMessage({ type: 'ignoreWord', word: wordInfo.word });
      hideSpellPopup();
    };
  }

  popup.style.display = 'block';

  const popupWidth = popup.offsetWidth || 150;
  const popupHeight = popup.offsetHeight || 130;

  let left = coords.left;
  let top = coords.top - popupHeight - 6;
  if (top < 10) {
    top = coords.bottom + 6;
  }
  if (left + popupWidth > window.innerWidth - 10) {
    left = Math.max(10, window.innerWidth - popupWidth - 10);
  }

  popup.style.left = left + 'px';
  popup.style.top = top + 'px';
}

function hideSpellPopup() {
  const popup = document.getElementById('spell-popup');
  if (popup) popup.style.display = 'none';
  currentSpellWordInfo = null;
}

function checkCaretSpelling() {
  const view = getEditorView();
  if (!view || !view.state) return;
  const { selection } = view.state;
  if (!selection || !selection.empty) {
    hideSpellPopup();
    return;
  }

  const wordInfo = getWordAtPos(view.state.doc, selection.from);
  if (!wordInfo || !isWordMisspelled(wordInfo.word)) {
    hideSpellPopup();
    return;
  }

  try {
    const coords = view.coordsAtPos(wordInfo.from);
    showSpellPopup(wordInfo, coords);
  } catch (err) {
    hideSpellPopup();
  }
}

// Custom Right-Click & Touchpad Context Formatting Menu
function setupContextMenu() {
  const menu = document.getElementById('format-menu');
  if (!menu) return;

  window.addEventListener('contextmenu', (e) => {
    e.preventDefault();
    hideSpellPopup();
    contextMenuX = e.clientX;
    contextMenuY = e.clientY;
    contextMenuCodeBlockEl = e.target && e.target.closest ? e.target.closest('.milkdown-code-block') : null;

    const isInsideCodeMirror = !!(e.target && e.target.closest && e.target.closest('.cm-editor'));
    const view = getEditorView();
    if (view && view.state && !isInsideCodeMirror) {
      try {
        const domSel = window.getSelection();
        const { from, to, empty } = view.state.selection;
        const hasTextSelected = (!empty && from !== to) || (domSel && !domSel.isCollapsed && domSel.toString().length > 0);

        let isInsideSelection = false;
        if (hasTextSelected) {
          if (domSel && domSel.rangeCount > 0) {
            const range = domSel.getRangeAt(0);
            const rects = range.getClientRects();
            for (let i = 0; i < rects.length; i++) {
              const r = rects[i];
              if (e.clientX >= r.left - 8 && e.clientX <= r.right + 8 &&
                  e.clientY >= r.top - 4 && e.clientY <= r.bottom + 4) {
                isInsideSelection = true;
                break;
              }
            }

            if (!isInsideSelection) {
              const b = range.getBoundingClientRect();
              if (e.clientY >= b.top - 4 && e.clientY <= b.bottom + 4 &&
                  e.clientX >= b.left - 20 && e.clientX <= b.right + 20) {
                isInsideSelection = true;
              }
            }
          }

          if (!isInsideSelection) {
            const posInfo = view.posAtCoords({ left: e.clientX, top: e.clientY });
            if (posInfo && typeof posInfo.pos === 'number') {
              if (posInfo.pos >= from && posInfo.pos <= to) {
                isInsideSelection = true;
              }
            }
          }
        }

        if (isInsideSelection) {
          const eff = getEffectiveSelection(view);
          if (!eff.empty && (view.state.selection.from !== eff.from || view.state.selection.to !== eff.to)) {
            try {
              const $f = view.state.doc.resolve(eff.from);
              const $t = view.state.doc.resolve(eff.to);
              view.dispatch(view.state.tr.setSelection(new TextSelection($f, $t)));
            } catch {}
          }
        } else {
          const posInfo = view.posAtCoords({ left: e.clientX, top: e.clientY });
          if (posInfo && typeof posInfo.pos === 'number') {
            const targetPos = Math.max(0, Math.min(posInfo.pos, view.state.doc.content.size));
            const $pos = view.state.doc.resolve(targetPos);
            const newSel = Selection.near($pos);
            view.dispatch(view.state.tr.setSelection(newSel));
          }
        }
        view.focus();
      } catch (err) {
        console.error('[contextmenu posAtCoords error]', err);
      }
    }

    showContextMenu(e.clientX, e.clientY);
  });

  window.addEventListener('pointerdown', (e) => {
    const spellPopup = document.getElementById('spell-popup');
    if (spellPopup && !spellPopup.contains(e.target)) {
      hideSpellPopup();
    }
    if (!menu.contains(e.target)) {
      hideContextMenu();
    }
  });

  window.addEventListener('pointerup', (e) => {
    const spellPopup = document.getElementById('spell-popup');
    if (spellPopup && spellPopup.contains(e.target)) return;
    if (menu && menu.style.display === 'block') return;
    clearTimeout(spellPopupTimer);
    spellPopupTimer = setTimeout(checkCaretSpelling, 120);
  });

  window.addEventListener('keyup', (e) => {
    if (e.key === 'ArrowLeft' || e.key === 'ArrowRight' || e.key === 'ArrowUp' || e.key === 'ArrowDown') {
      clearTimeout(spellPopupTimer);
      spellPopupTimer = setTimeout(checkCaretSpelling, 150);
    } else if (e.key === 'Escape') {
      hideSpellPopup();
      hideContextMenu();
    }
  });

  window.addEventListener('blur', () => {
    hideContextMenu();
  });

  menu.querySelectorAll('[data-action]').forEach((btn) => {
    btn.addEventListener('pointerdown', (e) => {
      e.preventDefault();
    });
    btn.addEventListener('mousedown', (e) => {
      e.preventDefault();
    });
    btn.addEventListener('click', (e) => {
      e.preventDefault();
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

  const view = getEditorView();
  const domSel = window.getSelection();
  const sel = domSel ? domSel.toString().trim() : '';
  const hasTextSelected = (view && view.state && !view.state.selection.empty) || (domSel && !domSel.isCollapsed && domSel.toString().length > 0);

  const cutBtn = document.getElementById('menu-cut');
  const copyBtn = document.getElementById('menu-copy');
  if (cutBtn) cutBtn.disabled = !hasTextSelected;
  if (copyBtn) copyBtn.disabled = !hasTextSelected;
  const ignoreBtn = document.getElementById('menu-ignore-word');
  const ignoreDiv = document.getElementById('menu-ignore-divider');
  const spellSection = document.getElementById('menu-spell-section');
  const spellList = document.getElementById('menu-spell-list');

  let activeWordInfo = null;
  if (view && view.state) {
    activeWordInfo = getWordAtPos(view.state.doc, view.state.selection.from);
  }

  if (activeWordInfo && isWordMisspelled(activeWordInfo.word)) {
    const suggestions = getSpellSuggestions(activeWordInfo.word);
    if (spellSection && spellList && suggestions.length > 0) {
      spellSection.style.display = 'block';
      spellList.innerHTML = '';
      suggestions.forEach((sugg) => {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'menu-spell-item';
        btn.textContent = sugg;
        btn.addEventListener('pointerdown', (e) => e.preventDefault());
        btn.addEventListener('click', (e) => {
          e.preventDefault();
          e.stopPropagation();
          replaceWord(activeWordInfo.from, activeWordInfo.to, sugg);
          hideContextMenu();
        });
        spellList.appendChild(btn);
      });
    }

    if (ignoreBtn) {
      ignoreBtn.style.display = 'flex';
      if (ignoreDiv) ignoreDiv.style.display = 'block';
      const label = ignoreBtn.querySelector('.label');
      if (label) label.textContent = `Ignore "${activeWordInfo.word}"`;
      ignoreBtn.dataset.word = activeWordInfo.word;
    }
  } else {
    if (spellSection) spellSection.style.display = 'none';
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
  }

  document.body.classList.add('context-menu-open');
  document.querySelectorAll('.milkdown-toolbar, .crepe-toolbar').forEach((tb) => {
    tb.setAttribute('data-show', 'false');
  });
  menu.style.display = 'block';

  const menuWidth = 250;
  const menuHeight = menu.offsetHeight || 420;
  const posX = (x + menuWidth > window.innerWidth) ? Math.max(10, window.innerWidth - menuWidth - 10) : x;
  const posY = (y + menuHeight > window.innerHeight) ? Math.max(10, window.innerHeight - menuHeight - 10) : y;

  menu.style.left = posX + 'px';
  menu.style.top = posY + 'px';
}

function hideContextMenu() {
  document.body.classList.remove('context-menu-open');
  const menu = document.getElementById('format-menu');
  if (menu) menu.style.display = 'none';
}

function extractInlineContent(schema, node) {
  if (!node || !node.content || node.content.size === 0) {
    return [];
  }
  const children = [];
  node.content.forEach((child) => {
    children.push(child);
  });

  // Strip leading manual markdown syntax if user typed or pasted raw markers like "- ", "* ", "1. ", "> ", "- [ ] "
  if (children.length > 0 && children[0].isText) {
    const rawText = children[0].text;
    const cleanedText = rawText.replace(/^([-*+]\s*\[\s*[xX]?\s*\]\s*|[-*+]\s+|>\s*|\d+\.\s*|#{1,6}\s+)/, '');
    if (cleanedText !== rawText) {
      if (cleanedText.length > 0) {
        children[0] = schema.text(cleanedText, children[0].marks);
      } else {
        children.shift();
      }
    }
  }

  return children;
}

function splitByHardBreaks(schema, inlineNodes) {
  const lines = [];
  let currentLine = [];

  for (const node of inlineNodes) {
    if (node.type && node.type.name === 'hard_break') {
      if (currentLine.length > 0) {
        lines.push(currentLine);
        currentLine = [];
      }
    } else {
      currentLine.push(node);
    }
  }
  if (currentLine.length > 0) {
    lines.push(currentLine);
  }

  return lines.length > 0 ? lines : [[]];
}

function convertBlock(view, targetType) {
  if (!view || !view.state) return false;
  const { state, dispatch } = view;
  const { schema } = state;

  const eff = getEffectiveSelection(view);
  let from = eff.from;
  let to = eff.to;
  let isRangeSelection = !eff.empty && from !== to;

  if (!isRangeSelection) {
    from = state.selection.from;
    to = state.selection.to;
    isRangeSelection = !state.selection.empty && from !== to;
  }

  // Collect all blocks in the selection
  const selectedBlocks = [];

  if (!isRangeSelection) {
    // Single cursor: find nearest textblock or list_item
    let $from = state.doc.resolve(from);
    let listItemDepth = -1;
    let blockquoteDepth = -1;
    let textblockDepth = -1;
    for (let d = $from.depth; d > 0; d--) {
      const node = $from.node(d);
      if (node.type === schema.nodes.list_item && listItemDepth < 0) {
        listItemDepth = d;
      }
      if (node.type === schema.nodes.blockquote && blockquoteDepth < 0) {
        blockquoteDepth = d;
      }
      if (node.isTextblock && textblockDepth < 0) {
        textblockDepth = d;
      }
    }

    if (listItemDepth > 0) {
      const parentList = $from.node(listItemDepth - 1);
      selectedBlocks.push({
        pos: $from.before(listItemDepth),
        end: $from.after(listItemDepth),
        node: $from.node(listItemDepth),
        type: 'list_item',
        parentType: parentList ? parentList.type.name : 'bullet_list',
        parentPos: parentList ? $from.before(listItemDepth - 1) : $from.before(listItemDepth),
        parentEnd: parentList ? $from.after(listItemDepth - 1) : $from.after(listItemDepth),
        parentList: parentList
      });
    } else if (textblockDepth > 0) {
      selectedBlocks.push({
        pos: blockquoteDepth > 0 ? $from.before(blockquoteDepth) : $from.before(textblockDepth),
        end: blockquoteDepth > 0 ? $from.after(blockquoteDepth) : $from.after(textblockDepth),
        node: $from.node(textblockDepth),
        type: $from.node(textblockDepth).type.name,
        parentType: blockquoteDepth > 0 ? 'blockquote' : 'doc',
        parentPos: blockquoteDepth > 0 ? $from.before(blockquoteDepth) : 0,
        parentEnd: blockquoteDepth > 0 ? $from.after(blockquoteDepth) : state.doc.content.size
      });
    }
  } else {
    // Multi-selection range: iterate document
    state.doc.forEach((topNode, topPos) => {
      const topEnd = topPos + topNode.nodeSize;
      if (topPos >= to || topEnd <= from) return;

      if (topNode.type === schema.nodes.bullet_list || topNode.type === schema.nodes.ordered_list) {
        topNode.forEach((itemNode, itemOffset) => {
          const itemPos = topPos + 1 + itemOffset;
          const itemEnd = itemPos + itemNode.nodeSize;
          if (itemPos < to && itemEnd > from) {
            selectedBlocks.push({
              pos: itemPos,
              end: itemEnd,
              node: itemNode,
              type: 'list_item',
              parentType: topNode.type.name,
              parentPos: topPos,
              parentEnd: topEnd,
              parentList: topNode
            });
          }
        });
      } else if (topNode.type === schema.nodes.blockquote) {
        topNode.forEach((childNode, childOffset) => {
          const childPos = topPos + 1 + childOffset;
          const childEnd = childPos + childNode.nodeSize;
          if (childPos < to && childEnd > from) {
            selectedBlocks.push({
              pos: childPos,
              end: childEnd,
              node: childNode,
              type: childNode.type.name,
              parentType: 'blockquote',
              parentPos: topPos,
              parentEnd: topEnd
            });
          }
        });
      } else if (topNode.isTextblock || topNode.type.name === 'paragraph' || topNode.type.name === 'heading' || topNode.type.name === 'code_block') {
        selectedBlocks.push({
          pos: topPos,
          end: topEnd,
          node: topNode,
          type: topNode.type.name,
          parentType: 'doc',
          parentPos: 0,
          parentEnd: state.doc.content.size
        });
      }
    });
  }

  if (selectedBlocks.length === 0) return false;

  // Extract content items from selected blocks
  const items = [];
  for (const block of selectedBlocks) {
    if (block.type === 'code_block') {
      const codeLines = (block.node.textContent || '').replace(/\r\n/g, '\n').split('\n');
      for (const codeLine of codeLines) {
        items.push({
          inlineNodes: codeLine ? [schema.text(codeLine)] : [],
          originalType: 'code_block',
          attrs: block.node.attrs || {},
          parentType: block.parentType,
          parentList: null,
          parentListNode: null,
          pos: block.pos,
          end: block.end,
          node: block.node
        });
      }
      continue;
    }

    let contentNode = block.node;
    if (block.type === 'list_item') {
      contentNode = block.node.firstChild || block.node;
    }
    const inlineNodes = extractInlineContent(schema, contentNode);
    const lines = splitByHardBreaks(schema, inlineNodes);
    for (const line of lines) {
      items.push({
        inlineNodes: line,
        originalType: block.type,
        attrs: block.node.attrs || {},
        parentType: block.parentType,
        parentList: block.parentList ? block.parentList.type.name : null,
        parentListNode: block.parentList,
        pos: block.pos,
        end: block.end,
        node: block.node
      });
    }
  }

  if (items.length === 0) return false;

  // Compute replacement range
  let replaceFrom = selectedBlocks[0].pos;
  let replaceTo = selectedBlocks[selectedBlocks.length - 1].end;

  // If first item starts a list and all items up to the list end are selected, expand range
  if (selectedBlocks[0].parentList && selectedBlocks[0].pos === selectedBlocks[0].parentPos + 1) {
    replaceFrom = selectedBlocks[0].parentPos;
  }
  if (selectedBlocks[selectedBlocks.length - 1].parentList && selectedBlocks[selectedBlocks.length - 1].end === selectedBlocks[selectedBlocks.length - 1].parentEnd - 1) {
    replaceTo = selectedBlocks[selectedBlocks.length - 1].parentEnd;
  }

  let tr = state.tr;

  if (targetType === 'bullet') {
    const allAlreadyBullet = items.every(it => (it.parentType === 'bullet_list' || it.parentList === 'bullet_list') && it.attrs.checked === null);
    if (allAlreadyBullet) {
      // Toggle off to plain paragraphs
      const paraNodes = items.map(it => schema.nodes.paragraph.create(null, it.inlineNodes));
      tr.replaceRangeWith(replaceFrom, replaceTo, paraNodes);
    } else {
      const itemNodes = items.map(it => schema.nodes.list_item.create({ checked: null, listType: 'bullet', spread: false }, schema.nodes.paragraph.create(null, it.inlineNodes)));
      const listNode = schema.nodes.bullet_list.create({ spread: false }, itemNodes);
      tr.replaceRangeWith(replaceFrom, replaceTo, listNode);
    }
  } else if (targetType === 'number') {
    const allAlreadyOrdered = items.every(it => it.parentType === 'ordered_list' || it.parentList === 'ordered_list');
    if (allAlreadyOrdered) {
      // Toggle off to plain paragraphs
      const paraNodes = items.map(it => schema.nodes.paragraph.create(null, it.inlineNodes));
      tr.replaceRangeWith(replaceFrom, replaceTo, paraNodes);
    } else {
      const itemNodes = items.map(it => schema.nodes.list_item.create({ checked: null, listType: 'ordered', spread: false }, schema.nodes.paragraph.create(null, it.inlineNodes)));
      const listNode = schema.nodes.ordered_list.create({ order: 1, spread: false }, itemNodes);
      tr.replaceRangeWith(replaceFrom, replaceTo, listNode);
    }
  } else if (targetType === 'todo') {
    const allAlreadyTodo = items.every(it => it.attrs.checked !== null && it.attrs.checked !== undefined);
    if (allAlreadyTodo) {
      const anyUnchecked = items.some(it => !it.attrs.checked);
      const newChecked = anyUnchecked;
      const itemNodes = items.map(it => schema.nodes.list_item.create({ checked: newChecked, listType: 'bullet', spread: false }, schema.nodes.paragraph.create(null, it.inlineNodes)));
      const listNode = schema.nodes.bullet_list.create({ spread: false }, itemNodes);
      tr.replaceRangeWith(replaceFrom, replaceTo, listNode);
    } else {
      const itemNodes = items.map(it => schema.nodes.list_item.create({ checked: false, listType: 'bullet', spread: false }, schema.nodes.paragraph.create(null, it.inlineNodes)));
      const listNode = schema.nodes.bullet_list.create({ spread: false }, itemNodes);
      tr.replaceRangeWith(replaceFrom, replaceTo, listNode);
    }
  } else if (targetType === 'quote') {
    const allAlreadyQuote = items.every(it => it.parentType === 'blockquote');
    if (allAlreadyQuote) {
      const paraNodes = items.map(it => schema.nodes.paragraph.create(null, it.inlineNodes));
      tr.replaceRangeWith(replaceFrom, replaceTo, paraNodes);
    } else {
      const paraNodes = items.map(it => schema.nodes.paragraph.create(null, it.inlineNodes));
      const bqNode = schema.nodes.blockquote.create(null, paraNodes);
      tr.replaceRangeWith(replaceFrom, replaceTo, bqNode);
    }
  } else if (targetType === 'paragraph') {
    const paraNodes = items.map(it => schema.nodes.paragraph.create(null, it.inlineNodes));
    tr.replaceRangeWith(replaceFrom, replaceTo, paraNodes);
    tr.setStoredMarks([]);
  } else if (targetType.startsWith('h')) {
    const level = parseInt(targetType.slice(1), 10) || 1;
    const allAlreadyHeading = items.every(it => it.originalType === 'heading' && it.attrs.level === level);
    if (allAlreadyHeading) {
      const paraNodes = items.map(it => schema.nodes.paragraph.create(null, it.inlineNodes));
      tr.replaceRangeWith(replaceFrom, replaceTo, paraNodes);
    } else {
      const headingNodes = items.map(it => schema.nodes.heading.create({ level }, it.inlineNodes));
      tr.replaceRangeWith(replaceFrom, replaceTo, headingNodes);
    }
  }

  const newPos = Math.min(replaceFrom + 1, tr.doc.content.size);
  tr.setSelection(Selection.near(tr.doc.resolve(newPos))).scrollIntoView();
  dispatch(tr);
  view.focus();
  return true;
}

function resetCodeBlockToParagraphs(view, codeBlockEl) {
  if (!view || !view.state) return false;
  const { state, dispatch } = view;
  const { schema } = state;
  const pType = schema.nodes.paragraph;
  if (!pType) return false;

  let targetPos = -1;
  let targetNode = null;

  if (codeBlockEl) {
    try {
      const domPos = view.posAtDOM(codeBlockEl, 0);
      const $pos = state.doc.resolve(Math.max(0, Math.min(domPos, state.doc.content.size)));
      for (let d = $pos.depth; d >= 0; d--) {
        const n = $pos.node(d);
        if (n.type.name === 'code_block') {
          targetPos = $pos.before(d);
          targetNode = n;
          break;
        }
      }
      if (!targetNode && $pos.nodeAfter && $pos.nodeAfter.type.name === 'code_block') {
        targetPos = $pos.pos;
        targetNode = $pos.nodeAfter;
      }
    } catch {}
  }

  if (!targetNode) {
    const $from = state.selection.$from;
    for (let d = $from.depth; d > 0; d--) {
      if ($from.node(d).type.name === 'code_block') {
        targetPos = $from.before(d);
        targetNode = $from.node(d);
        break;
      }
    }
  }

  if (!targetNode || targetPos < 0) return false;

  const lines = (targetNode.textContent || '').replace(/\r\n/g, '\n').split('\n');
  const paraNodes = lines.map(l => pType.create(null, l ? schema.text(l) : null));
  let tr = state.tr.replaceRangeWith(targetPos, targetPos + targetNode.nodeSize, paraNodes);
  tr = tr.setStoredMarks([]);
  const selPos = Math.min(targetPos + 1, tr.doc.content.size);
  tr = tr.setSelection(Selection.near(tr.doc.resolve(selPos))).scrollIntoView();
  dispatch(tr);
  view.focus();
  return true;
}

function getMarkedSpanAroundPos(doc, pos) {
  const $pos = doc.resolve(pos);
  const parent = $pos.parent;
  if (!parent || !parent.isTextblock || parent.content.size === 0) return null;

  const parentStart = $pos.start();
  const offset = $pos.parentOffset;

  // Walk child nodes in this textblock to find the child at or immediately before caret
  let targetIdx = -1;
  let running = 0;
  const children = [];
  parent.forEach((child, childOffset) => {
    children.push({ node: child, start: parentStart + childOffset, end: parentStart + childOffset + child.nodeSize, relStart: childOffset, relEnd: childOffset + child.nodeSize });
  });

  for (let i = 0; i < children.length; i++) {
    const c = children[i];
    if (offset > c.relStart && offset < c.relEnd) {
      targetIdx = i;
      break;
    }
    if (offset === c.relEnd && c.node.marks && c.node.marks.length > 0) {
      targetIdx = i;
      break;
    }
    if (offset === c.relStart && c.node.marks && c.node.marks.length > 0) {
      targetIdx = i;
      break;
    }
  }

  if (targetIdx < 0 || !children[targetIdx].node.marks || children[targetIdx].node.marks.length === 0) {
    return null;
  }

  let left = targetIdx;
  while (left > 0 && children[left - 1].node.marks && children[left - 1].node.marks.length > 0) {
    left--;
  }
  let right = targetIdx;
  while (right < children.length - 1 && children[right + 1].node.marks && children[right + 1].node.marks.length > 0) {
    right++;
  }

  return { from: children[left].start, to: children[right].end };
}

function clearFormattingAndReset(view, codeBlockElOverride) {
  if (!view || !view.state) return;

  if (codeBlockElOverride || isInsideCodeBlock(view)) {
    if (resetCodeBlockToParagraphs(view, codeBlockElOverride)) {
      return;
    }
  }

  let state = view.state;
  const eff = getEffectiveSelection(view);
  let from = eff.from;
  let to = eff.to;
  let wasEmpty = eff.empty || from === to;

  const isBlockResetNeeded = (() => {
    const $pos = state.doc.resolve(Math.min(from, state.doc.content.size));
    for (let d = $pos.depth; d > 0; d--) {
      const n = $pos.node(d).type.name;
      if (n === 'list_item' || n === 'blockquote' || n === 'heading' || n === 'code_block') return true;
    }
    return false;
  })();

  if (wasEmpty) {
    const $f = state.doc.resolve(from);
    if (isBlockResetNeeded && $f.parent && $f.parent.isTextblock) {
      from = $f.start();
      to = $f.end();
    } else {
      const span = getMarkedSpanAroundPos(state.doc, from);
      if (span) {
        from = span.from;
        to = span.to;
      } else if ($f.parent && $f.parent.isTextblock) {
        from = $f.start();
        to = $f.end();
      }
    }
  }

  let tr = state.tr;
  if (to > from) {
    Object.values(state.schema.marks).forEach((markType) => {
      if (markType) {
        tr = tr.removeMark(from, to, markType);
      }
    });
  }
  tr = tr.setStoredMarks([]);
  view.dispatch(tr);

  // Also reset block to plain <p> if caret was empty OR selection covered non-paragraph block(s)
  state = view.state;
  const $afterFrom = state.doc.resolve(Math.min(from, state.doc.content.size));
  const $afterTo = state.doc.resolve(Math.min(to, state.doc.content.size));
  const parentFrom = $afterFrom.parent;
  const isInsideListOrQuote = (() => {
    for (let d = $afterFrom.depth; d > 0; d--) {
      const n = $afterFrom.node(d).type.name;
      if (n === 'list_item' || n === 'blockquote' || n === 'heading' || n === 'code_block') return true;
    }
    return false;
  })();

  const coversWholeBlock = parentFrom && from <= $afterFrom.start() && to >= $afterFrom.end();
  const spansMultipleBlocks = !$afterFrom.sameParent($afterTo);

  if (isInsideListOrQuote && (wasEmpty || coversWholeBlock || spansMultipleBlocks)) {
    convertBlock(view, 'paragraph');
  } else {
    view.focus();
  }
}

function pastePlainText(view, rawText) {
  if (!view || !view.state || !rawText) return;
  const { state, dispatch } = view;
  const eff = getEffectiveSelection(view);
  const normalized = rawText.replace(/\r\n/g, '\n');

  let tr = state.tr.setStoredMarks([]);
  if (normalized.includes('\n') && !isInsideCodeBlock(view)) {
    const pType = state.schema.nodes.paragraph;
    if (pType) {
      const lines = normalized.split('\n');
      const paraNodes = lines.map(l => pType.create(null, l ? state.schema.text(l) : null));
      tr = tr.replaceRangeWith(eff.from, eff.to, paraNodes);
    } else {
      tr = tr.replaceWith(eff.from, eff.to, state.schema.text(normalized));
    }
  } else {
    tr = tr.replaceWith(eff.from, eff.to, state.schema.text(normalized));
  }
  tr = tr.setStoredMarks([]);
  dispatch(tr.scrollIntoView());
  view.focus();
}

function executeAction(action) {
  if (!crepeInstance) return;
  const view = crepeInstance.editor.ctx.get(editorViewCtx);
  if (!view) return;
  const activeCodeBlockEl = contextMenuCodeBlockEl;
  contextMenuCodeBlockEl = null;

  switch (action) {
    case 'cut': {
      const domSel = window.getSelection();
      const selText = (domSel && domSel.toString()) || '';
      if (selText) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(selText).then(() => {
            if (view && view.state && !view.state.selection.empty) {
              view.dispatch(view.state.tr.deleteSelection().scrollIntoView());
            } else {
              document.execCommand('cut');
            }
          }).catch(() => {
            document.execCommand('cut');
          });
        } else {
          document.execCommand('cut');
        }
      }
      view.focus();
      break;
    }
    case 'copy': {
      const domSel = window.getSelection();
      const selText = (domSel && domSel.toString()) || '';
      if (selText) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(selText).catch(() => {
            document.execCommand('copy');
          });
        } else {
          document.execCommand('copy');
        }
      }
      view.focus();
      break;
    }
    case 'paste': {
      if (navigator.clipboard && navigator.clipboard.readText) {
        navigator.clipboard.readText().then((text) => {
          if (text) {
            if (!insertParsedMarkdown(view, text)) {
              view.dispatch(view.state.tr.insertText(text).scrollIntoView());
            }
          }
        }).catch(() => {
          document.execCommand('paste');
        });
      } else {
        document.execCommand('paste');
      }
      view.focus();
      break;
    }
    case 'paste-plain': {
      if (navigator.clipboard && navigator.clipboard.readText) {
        navigator.clipboard.readText().then((text) => {
          if (text) {
            pastePlainText(view, text);
          }
        }).catch(() => {
          view.focus();
        });
      }
      view.focus();
      break;
    }
    case 'select-all': {
      const tr = view.state.tr.setSelection(new AllSelection(view.state.doc));
      view.dispatch(tr);
      view.focus();
      break;
    }
    case 'paragraph':
      if (activeCodeBlockEl || isInsideCodeBlock(view)) {
        resetCodeBlockToParagraphs(view, activeCodeBlockEl);
      } else {
        convertBlock(view, 'paragraph');
      }
      break;
    case 'clear-format':
      clearFormattingAndReset(view, activeCodeBlockEl);
      break;
    case 'h1':
    case 'h2':
    case 'h3':
    case 'h4':
    case 'h5':
    case 'h6':
    case 'todo':
    case 'bullet':
    case 'number':
    case 'quote':
      convertBlock(view, action);
      break;
    case 'code': {
      const eff = getEffectiveSelection(view);
      if (!eff.empty && eff.from !== eff.to) {
        const selectedText = view.state.doc.textBetween(eff.from, eff.to, '\n');
        const codeNode = view.state.schema.nodes.code_block.create(null, selectedText ? view.state.schema.text(selectedText) : null);
        view.dispatch(view.state.tr.replaceRangeWith(eff.from, eff.to, codeNode).scrollIntoView());
        view.focus();
      } else {
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
      }
      break;
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
  let { from, to, empty } = getEffectiveSelection(view);

  // If selection is empty, automatically expand to the word at caret
  if (empty) {
    const $from = state.selection.$from;
    const parent = $from.parent;
    if (parent && parent.isTextblock) {
      const text = parent.textContent;
      const offset = $from.parentOffset;
      let start = offset;
      let end = offset;
      while (start > 0 && /\S/.test(text[start - 1])) {
        start--;
      }
      while (end < text.length && /\S/.test(text[end])) {
        end++;
      }
      if (start < end) {
        from = $from.start() + start;
        to = $from.start() + end;
        empty = false;
      }
    }
  }

  if (empty) return;

  let markType = state.schema.marks[markName];
  if (!markType && (markName === 'code_inline' || markName === 'inline-code')) markType = state.schema.marks.inlineCode;
  if (!markType && markName === 'inlineCode') markType = state.schema.marks.code_inline;
  if (!markType && markName === 'em') markType = state.schema.marks.emphasis;
  if (!markType && (markName === 'strike' || markName === 'strike_through')) markType = state.schema.marks.strike_through;
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
    } else if (window.postAvWebViewMessage) {
      if (typeof window.postAvWebViewMessage.PostMessage === 'function') {
        window.postAvWebViewMessage.PostMessage(payload);
      } else if (typeof window.postAvWebViewMessage.postMessage === 'function') {
        window.postAvWebViewMessage.postMessage(payload);
      }
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

function updateResponsiveImages() {
  const imageBlocks = document.querySelectorAll('.milkdown-image-block');
  imageBlocks.forEach((block) => {
    const wrapper = block.querySelector('.image-wrapper');
    const img = block.querySelector('img');
    if (!img) return;

    img.style.maxWidth = '100%';
    img.style.height = 'auto';
    img.style.objectFit = 'contain';

    const alt = img.getAttribute('alt');
    const ratioNum = alt ? parseFloat(alt) : NaN;
    if (!isNaN(ratioNum) && ratioNum > 0 && ratioNum < 1) {
      const pct = Math.min(100, Math.max(10, Math.round(ratioNum * 100)));
      if (wrapper) wrapper.style.maxWidth = `${pct}%`;
    } else {
      if (wrapper) wrapper.style.maxWidth = '100%';
    }
  });
}

window.addEventListener('resize', updateResponsiveImages);

function resolveAllImagesInDOM() {
  const imgs = document.querySelectorAll('.milkdown img, .ProseMirror img, img');
  imgs.forEach(resolveImageElement);
  updateResponsiveImages();
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
      updateResponsiveImages();
    } else if (m.type === 'attributes' && m.attributeName === 'src') {
      if (m.target && m.target.tagName === 'IMG') {
        resolveImageElement(m.target);
      }
      updateResponsiveImages();
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
      let newTr = tr.replaceSelectionWith(node);
      const pType = schema.nodes.paragraph;
      if (pType) {
        const afterPos = newTr.selection.to;
        const emptyPara = pType.create();
        newTr = newTr.insert(afterPos, emptyPara);
        const insidePos = afterPos + 1;
        newTr = newTr.setSelection(Selection.near(newTr.doc.resolve(insidePos)));
      }
      newTr = newTr.scrollIntoView();
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

// Check if the current cursor / active element is inside a Code Block (CodeMirror)
function isInsideCodeBlock(view) {
  const active = document.activeElement;
  if (active && active.closest && (active.closest('.cm-editor') || active.closest('.code-block') || active.closest('pre'))) {
    return true;
  }
  if (!view || !view.state) return false;
  const { selection } = view.state;
  if (!selection) return false;
  const $from = selection.$from;
  if ($from) {
    if ($from.node()?.type?.spec?.code) return true;
    if ($from.parent?.type?.name?.includes('code_block') || $from.parent?.type?.name?.includes('code_bracket')) return true;
  }
  return false;
}

// Detect if text contains Markdown formatting syntax
function isMarkdownSyntax(text) {
  if (!text || typeof text !== 'string') return false;
  // 1. Headings: # Title, ## Subtitle, etc.
  if (/^#{1,6}\s+\S+/m.test(text)) return true;
  // 2. Task lists: - [ ] or * [x] or + [ ]
  if (/^[-*+]\s+\[[ xX]\]/m.test(text)) return true;
  // 3. Unordered lists: - item, * item, + item
  if (/^[-*+]\s+\S+/m.test(text)) return true;
  // 4. Ordered lists: 1. item
  if (/^\d+\.\s+\S+/m.test(text)) return true;
  // 5. Blockquotes: > quote
  if (/^>\s+\S+/m.test(text)) return true;
  // 6. Tables: | col | col |
  if (/^\|.+?\|.+?\|\s*$/m.test(text)) return true;
  // 7. Code fences: ```lang
  if (/^```/m.test(text)) return true;
  // 8. Horizontal rules: --- or ***
  if (/^([-*_]){3,}\s*$/m.test(text)) return true;
  // 9. Markdown links or images: [text](url) or ![alt](url)
  if (/!?\[[^\]]+\]\([^)]+\)/.test(text)) return true;
  // 10. Markdown inline formatting spanning words: **bold**, *italic*, ~~strike~~
  if (/(\*\*[^*]+\*\*|\*[^*]+\*|~~[^~]+~~)/.test(text)) return true;

  return false;
}

// Parse markdown text using Milkdown's parser and insert as ProseMirror blocks
function insertParsedMarkdown(view, markdownText) {
  if (!view || !view.state || !crepeInstance || !crepeInstance.editor) return false;
  try {
    const parser = crepeInstance.editor.ctx.get(parserCtx);
    const schema = crepeInstance.editor.ctx.get(schemaCtx);
    if (!parser || !schema) return false;

    const parsedDoc = parser(markdownText);
    if (!parsedDoc || typeof parsedDoc === 'string') return false;

    const dom = DOMSerializer.fromSchema(schema).serializeFragment(parsedDoc.content);
    const domParser = DOMParser.fromSchema(schema);
    const slice = domParser.parseSlice(dom);

    // If slice is just a single text node without marks, replace selection with text node
    if (slice && slice.content && slice.content.childCount === 1) {
      const node = slice.content.firstChild;
      if (node?.type?.name === 'text' && (!node.marks || node.marks.length === 0)) {
        view.dispatch(view.state.tr.replaceSelectionWith(node, true).scrollIntoView());
        return true;
      }
      if (node?.type?.name === 'paragraph' && node.childCount === 1) {
        const _node = node.firstChild;
        if (_node?.type?.name === 'text' && (!_node.marks || _node.marks.length === 0)) {
          view.dispatch(view.state.tr.replaceSelectionWith(_node, true).scrollIntoView());
          return true;
        }
      }
    }

    view.dispatch(view.state.tr.replaceSelection(slice).scrollIntoView());
    return true;
  } catch (err) {
    console.warn('Error inserting parsed markdown:', err);
    return false;
  }
}

// Unified paste handler for Images, URLs, Markdown, and Inline Code
function handlePaste(e) {
  const view = getEditorView();

  // If inside a Code Block (CodeMirror), let CodeMirror handle paste natively
  if (isInsideCodeBlock(view)) {
    return;
  }

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

  if (!view || !view.state) return;
  const { state, dispatch } = view;
  const { from, to, empty } = getEffectiveSelection(view);

  const rawText = e.clipboardData ? e.clipboardData.getData('text/plain') : '';
  if (!rawText) return;

  // Ctrl+Shift+V: Paste as plain text without any inline/block formatting
  if (Date.now() - lastPlainPasteTimestamp < 400) {
    lastPlainPasteTimestamp = 0;
    e.preventDefault();
    e.stopPropagation();
    e.stopImmediatePropagation();
    pastePlainText(view, rawText);
    return;
  }

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

  // 3. Markdown / Plain Text paste handling:
  // Check if clipboard is from VS Code (which tags vscode-editor-data and causes Milkdown to force code blocks)
  let isFromVSCode = false;
  let vscodeMode = '';
  try {
    const vscodeData = e.clipboardData ? e.clipboardData.getData('vscode-editor-data') : '';
    if (vscodeData) {
      isFromVSCode = true;
      vscodeMode = JSON.parse(vscodeData)?.mode || '';
    }
  } catch {}

  const isMd = isMarkdownSyntax(rawText);
  const isVSCodeMarkdownOrText = isFromVSCode && (vscodeMode === 'markdown' || vscodeMode === 'md' || vscodeMode === 'plaintext' || vscodeMode === 'text');

  if (isMd || isVSCodeMarkdownOrText) {
    const handled = insertParsedMarkdown(view, rawText);
    if (handled) {
      e.preventDefault();
      e.stopPropagation();
      e.stopImmediatePropagation();

      setTimeout(() => {
        const currentView = getEditorView();
        if (currentView) autoFormatInlineCode(currentView);
        resolveAllImagesInDOM();
      }, 10);
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
window.executeAction = function(action) {
  if (action === 'numbered') action = 'number';
  executeAction(action);
};
window.focusEditor = function() {
  const view = getEditorView();
  if (view) {
    view.focus();
  }
};

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', init);
} else {
  init();
}
