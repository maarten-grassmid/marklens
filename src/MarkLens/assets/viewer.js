/* MarkLens - preview page logic. */
(function () {
  'use strict';

  const ASSETS = 'https://assets.marklens/';
  const $ = (id) => document.getElementById(id);
  const scroller = $('scroller');
  const content = $('content');
  const tocList = $('toc-list');
  const menu = $('menu');

  const state = {
    settings: { theme: 'light', toc: true, tocWidth: 240, sync: true, highlight: false, mermaid: false, math: false },
    docKey: null,
    lastHtml: '',
    scrollMemory: new Map(),
    ignoreScrollUntil: 0,
    anchors: null,          // cached [{el, line, top}] for scroll mapping
    headings: [],
    activeHeading: null,
  };

  const host = {
    post(msg) {
      // Line numbers refer to a specific render; let the host reject stale ones.
      if (state.version !== undefined) msg.v = state.version;
      if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(msg);
    },
  };

  // ------------------------------------------------------------------ lazy library loading

  const loaded = new Map();
  function loadScript(path) {
    if (!loaded.has(path)) {
      loaded.set(path, new Promise((resolve, reject) => {
        const s = document.createElement('script');
        s.src = ASSETS + path;
        s.onload = resolve;
        s.onerror = () => { loaded.delete(path); reject(new Error('failed to load ' + path)); };
        document.head.appendChild(s);
      }));
    }
    return loaded.get(path);
  }

  function setStylesheet(id, path) {
    const link = $(id);
    if (!path) { link.disabled = true; return; }
    const href = ASSETS + path;
    if (link.getAttribute('href') !== href) link.setAttribute('href', href);
    link.disabled = false;
  }

  // ------------------------------------------------------------------ messages from host

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', (e) => {
      const msg = e.data;
      try {
        switch (msg.type) {
          case 'render': render(msg); break;
          case 'placeholder': placeholder(msg); break;
          case 'scrollTo': scrollToLine(msg.line, false); break;
          case 'settings': applySettings(msg); break;
        }
      } catch (err) {
        console.error(err);
      }
    });
  }

  function applySettings(s) {
    const prev = state.settings;
    state.settings = s;

    document.body.classList.toggle('theme-dark', s.theme === 'dark');
    document.body.classList.toggle('theme-light', s.theme !== 'dark');
    document.body.classList.toggle('no-toc', !s.toc);
    $('app').style.setProperty('--toc-width', (s.tocWidth || 240) + 'px');
    $('custom-css').textContent = s.customCss || '';

    setStylesheet('hl-theme', s.highlight
      ? (s.theme === 'dark' ? 'vendor/highlight/github-dark.min.css' : 'vendor/highlight/github.min.css')
      : null);
    setStylesheet('katex-css', s.math ? 'vendor/katex/katex.min.css' : null);

    const needsRebuild = prev.theme !== s.theme || prev.highlight !== s.highlight ||
      prev.mermaid !== s.mermaid || prev.math !== s.math;
    if (needsRebuild && state.lastHtml) {
      const top = scroller.scrollTop;
      content.textContent = '';
      patch(state.lastHtml);
      scroller.scrollTop = top;
    }
    invalidateAnchors();
  }

  // ------------------------------------------------------------------ rendering

  function placeholder(msg) {
    saveScroll();
    state.docKey = null;
    state.version = undefined;
    state.lastHtml = '';
    content.innerHTML = '';
    const div = document.createElement('div');
    div.className = 'placeholder';
    const t = document.createElement('div');
    t.className = 'title';
    t.textContent = msg.text;
    const h = document.createElement('div');
    h.textContent = msg.hint || '';
    div.append(t, h);
    content.appendChild(div);
    buildToc();
  }

  function saveScroll() {
    if (state.docKey) state.scrollMemory.set(state.docKey, scroller.scrollTop);
  }

  function render(msg) {
    const switching = msg.docKey !== state.docKey;
    if (switching) {
      saveScroll();
      content.textContent = '';
    }
    state.docKey = msg.docKey;
    state.version = msg.v;
    state.lastHtml = msg.html;
    document.title = msg.title || 'Markdown Viewer';

    patch(msg.html);
    buildToc();
    invalidateAnchors();

    if (switching) {
      const remembered = state.scrollMemory.get(msg.docKey);
      if (remembered !== undefined) {
        setScrollTop(remembered);
      } else if (msg.line >= 0) {
        scrollToLine(msg.line, false);
      } else {
        setScrollTop(0);
      }
    }
    updateActiveHeading();
  }

  const LINE_ATTRS = / data-line(?:-end)?="\d+"/g;
  function keyOf(node) {
    return node.nodeType === 1
      ? node.outerHTML.replace(LINE_ATTRS, '')
      : '#' + node.nodeType + ':' + node.textContent;
  }

  /**
   * Replaces the content with new HTML, reusing unchanged top-level blocks. Only the
   * changed middle section is rebuilt, which keeps typing in large documents fast and
   * avoids re-running highlighting / Mermaid / KaTeX for untouched blocks.
   */
  function patch(html) {
    const tpl = document.createElement('template');
    tpl.innerHTML = html;
    const next = Array.from(tpl.content.childNodes);
    const nextKeys = next.map(keyOf);
    const prev = Array.from(content.childNodes);
    const prevKeys = prev.map((n) => n.__marklensKey);

    let start = 0;
    const max = Math.min(prev.length, next.length);
    while (start < max && prevKeys[start] === nextKeys[start]) start++;
    let end = 0;
    while (end < max - start &&
           prevKeys[prev.length - 1 - end] === nextKeys[next.length - 1 - end]) end++;

    // Unchanged blocks keep their DOM, but line numbers may have shifted.
    for (let i = 0; i < start; i++) syncLines(prev[i], next[i]);
    for (let i = 0; i < end; i++) syncLines(prev[prev.length - 1 - i], next[next.length - 1 - i]);

    const anchor = end > 0 ? prev[prev.length - end] : null;
    for (let i = start; i < prev.length - end; i++) prev[i].remove();

    const added = [];
    for (let i = start; i < next.length - end; i++) {
      const node = next[i];
      node.__marklensKey = nextKeys[i];
      content.insertBefore(node, anchor);
      if (node.nodeType === 1) added.push(node);
    }
    added.forEach(enhance);
  }

  function syncLines(oldNode, newNode) {
    if (oldNode.nodeType !== 1) return;
    const a = [oldNode, ...oldNode.querySelectorAll('[data-line]')].filter((e) => e.hasAttribute('data-line'));
    const b = [newNode, ...newNode.querySelectorAll('[data-line]')].filter((e) => e.hasAttribute('data-line'));
    const n = Math.min(a.length, b.length);
    for (let i = 0; i < n; i++) {
      a[i].setAttribute('data-line', b[i].getAttribute('data-line'));
      a[i].setAttribute('data-line-end', b[i].getAttribute('data-line-end'));
    }
  }

  function enhance(root) {
    // task list checkboxes become interactive and write back to the source
    root.querySelectorAll('li.task-list-item input[type=checkbox]').forEach((cb) => {
      cb.removeAttribute('disabled');
    });

    // copy buttons on code blocks
    const pres = (root.matches('pre') ? [root] : Array.from(root.querySelectorAll('pre')))
      .filter((pre) => !pre.classList.contains('mermaid'));
    pres.forEach((pre) => {
      if (pre.querySelector(':scope > .code-copy')) return;
      const btn = document.createElement('button');
      btn.className = 'code-copy';
      btn.textContent = 'Copy';
      btn.title = 'Copy code';
      pre.appendChild(btn);
    });

    const s = state.settings;
    if (s.highlight) highlight(root);
    if (s.math) renderMath(root);
    if (s.mermaid) renderMermaid(root);
  }

  function highlight(root) {
    const blocks = Array.from(root.querySelectorAll('pre > code[class*="language-"]'));
    if (!blocks.length) return;
    loadScript('vendor/highlight/highlight.min.js').then(() => {
      const hljs = window.hljs;
      hljs.configure({ ignoreUnescapedHTML: true });
      let i = 0;
      const step = () => {
        const until = performance.now() + 12;
        while (i < blocks.length && performance.now() < until) {
          const code = blocks[i++];
          const lang = (code.className.match(/language-([\w+#-]+)/) || [])[1];
          if (lang && hljs.getLanguage(lang) && code.isConnected) hljs.highlightElement(code);
        }
        if (i < blocks.length) requestAnimationFrame(step); else invalidateAnchors();
      };
      step();
    }).catch(console.error);
  }

  function renderMath(root) {
    const nodes = Array.from(root.querySelectorAll('.math'));
    if (root.classList && root.classList.contains('math')) nodes.unshift(root);
    if (!nodes.length) return;
    loadScript('vendor/katex/katex.min.js').then(() => {
      nodes.forEach((el) => {
        if (el.__marklensMath) return;
        el.__marklensMath = true;
        let tex = el.textContent.trim();
        const display = el.tagName === 'DIV';
        tex = tex.replace(/^\\\(|\\\)$/g, '').replace(/^\\\[|\\\]$/g, '');
        try {
          window.katex.render(tex, el, { displayMode: display, throwOnError: false });
        } catch (err) {
          el.title = String(err);
        }
      });
      invalidateAnchors();
    }).catch(console.error);
  }

  let mermaidReady = null;
  let mermaidSeq = 0;
  function renderMermaid(root) {
    const nodes = Array.from(root.querySelectorAll('.mermaid'));
    if (root.matches && root.matches('.mermaid')) nodes.unshift(root);
    if (!nodes.length) return;
    if (!mermaidReady) mermaidReady = loadScript('vendor/mermaid/mermaid.min.js');
    mermaidReady.then(async () => {
      const mermaid = window.mermaid;
      mermaid.initialize({
        startOnLoad: false,
        securityLevel: 'strict',
        theme: state.settings.theme === 'dark' ? 'dark' : 'default',
      });
      for (const el of nodes) {
        if (!el.isConnected || el.__marklensMermaid) continue;
        el.__marklensMermaid = true;
        const src = el.textContent;
        try {
          const { svg } = await mermaid.render('marklens-mermaid-' + (++mermaidSeq), src);
          el.innerHTML = svg;
        } catch (err) {
          el.innerHTML = '';
          const pre = document.createElement('pre');
          pre.textContent = 'Mermaid error: ' + (err && err.message ? err.message : err) + '\n\n' + src;
          el.appendChild(pre);
        }
      }
      invalidateAnchors();
    }).catch(console.error);
  }

  // ------------------------------------------------------------------ scroll mapping

  function invalidateAnchors() {
    state.anchors = null;
  }

  new ResizeObserver(invalidateAnchors).observe(content);
  content.addEventListener('load', invalidateAnchors, true); // images

  function anchors() {
    if (state.anchors) return state.anchors;
    const base = scroller.getBoundingClientRect().top - scroller.scrollTop;
    const list = [];
    let lastLine = -1;
    for (const el of content.querySelectorAll('[data-line]')) {
      const line = +el.getAttribute('data-line');
      if (line < lastLine) continue; // e.g. footnotes rendered out of source order
      const rect = el.getBoundingClientRect();
      if (rect.height === 0 && rect.width === 0) continue;
      list.push({ el, line, end: +el.getAttribute('data-line-end'), top: rect.top - base, height: rect.height });
      lastLine = line;
    }
    state.anchors = list;
    return list;
  }

  function setScrollTop(y) {
    state.ignoreScrollUntil = performance.now() + 120;
    scroller.scrollTop = y;
  }

  function scrollToLine(line, notify) {
    const list = anchors();
    if (!list.length) return;
    let lo = 0, hi = list.length - 1, idx = 0;
    while (lo <= hi) {
      const mid = (lo + hi) >> 1;
      if (list[mid].line <= line) { idx = mid; lo = mid + 1; } else hi = mid - 1;
    }
    const a = list[idx];
    let y;
    if (line < a.line) {
      y = 0;
    } else {
      const b = list[idx + 1];
      if (b && b.line > a.line) {
        const span = b.line - a.line;
        y = a.top + (b.top - a.top) * Math.min(1, (line - a.line) / span);
      } else {
        const span = Math.max(1, a.end - a.line + 1);
        y = a.top + a.height * Math.min(1, (line - a.line) / span);
      }
    }
    if (notify) scroller.scrollTop = y; else setScrollTop(y);
  }

  function lineAtScroll() {
    const list = anchors();
    if (!list.length) return 0;
    const y = scroller.scrollTop;
    if (y <= 0) return 0;
    let lo = 0, hi = list.length - 1, idx = 0;
    while (lo <= hi) {
      const mid = (lo + hi) >> 1;
      if (list[mid].top <= y) { idx = mid; lo = mid + 1; } else hi = mid - 1;
    }
    const a = list[idx];
    const b = list[idx + 1];
    if (b && b.top > a.top) {
      return a.line + (b.line - a.line) * Math.min(1, (y - a.top) / (b.top - a.top));
    }
    return a.line + (a.end - a.line + 1) * Math.min(1, (y - a.top) / Math.max(1, a.height));
  }

  let scrollFrame = 0;
  scroller.addEventListener('scroll', () => {
    if (scrollFrame) return;
    scrollFrame = requestAnimationFrame(() => {
      scrollFrame = 0;
      updateActiveHeading();
      if (performance.now() < state.ignoreScrollUntil) return;
      if (state.settings.sync && state.docKey) host.post({ type: 'scroll', line: Math.floor(lineAtScroll()) });
    });
  }, { passive: true });

  // ------------------------------------------------------------------ table of contents

  function buildToc() {
    const heads = Array.from(content.querySelectorAll('h1, h2, h3, h4, h5, h6'));
    state.headings = heads;
    state.activeHeading = null;
    tocList.textContent = '';
    $('toc-empty').hidden = heads.length > 0;
    if (!heads.length) return;

    const minLevel = Math.min(...heads.map((h) => +h.tagName[1]));
    const frag = document.createDocumentFragment();
    heads.forEach((h, i) => {
      const level = +h.tagName[1];
      const li = document.createElement('li');
      li.className = 'h' + (level - minLevel + 1);
      const a = document.createElement('a');
      a.textContent = h.textContent.trim() || '(untitled)';
      a.title = a.textContent;
      a.style.setProperty('--level', level - minLevel);
      a.dataset.index = i;
      li.appendChild(a);
      frag.appendChild(li);
    });
    tocList.appendChild(frag);
    filterToc();
  }

  tocList.addEventListener('click', (e) => {
    const a = e.target.closest('a');
    if (!a) return;
    const h = state.headings[+a.dataset.index];
    if (h) revealElement(h);
  });

  function revealElement(el) {
    el.scrollIntoView({ block: 'start' });
    el.classList.remove('flash');
    void el.offsetWidth;
    el.classList.add('flash');
  }

  function updateActiveHeading() {
    if (!state.headings.length || !state.settings.toc) return;
    const top = scroller.getBoundingClientRect().top + 24;
    let active = 0;
    for (let i = 0; i < state.headings.length; i++) {
      if (state.headings[i].getBoundingClientRect().top <= top) active = i; else break;
    }
    if (state.activeHeading === active) return;
    state.activeHeading = active;
    tocList.querySelectorAll('a.active').forEach((a) => a.classList.remove('active'));
    const link = tocList.querySelector(`a[data-index="${active}"]`);
    if (link) {
      link.classList.add('active');
      const lr = link.getBoundingClientRect();
      const tr = tocList.getBoundingClientRect();
      if (lr.top < tr.top || lr.bottom > tr.bottom) link.scrollIntoView({ block: 'nearest' });
    }
  }

  const tocFilter = $('toc-filter');
  function filterToc() {
    const q = tocFilter.value.trim().toLowerCase();
    tocList.querySelectorAll('li').forEach((li) => {
      li.classList.toggle('hidden', q !== '' && !li.textContent.toLowerCase().includes(q));
    });
  }
  tocFilter.addEventListener('input', filterToc);
  tocFilter.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') {
      const first = tocList.querySelector('li:not(.hidden) a');
      if (first) first.click();
    } else if (e.key === 'Escape') {
      tocFilter.value = '';
      filterToc();
    }
  });

  $('toc-close').addEventListener('click', () => host.post({ type: 'toc', show: false }));

  // resizable sidebar
  const resizer = $('toc-resizer');
  resizer.addEventListener('pointerdown', (e) => {
    resizer.setPointerCapture(e.pointerId);
    resizer.classList.add('dragging');
    const move = (ev) => {
      const w = Math.max(120, Math.min(800, ev.clientX));
      $('app').style.setProperty('--toc-width', w + 'px');
      state.settings.tocWidth = w;
    };
    const up = () => {
      resizer.classList.remove('dragging');
      resizer.removeEventListener('pointermove', move);
      resizer.removeEventListener('pointerup', up);
      invalidateAnchors();
      host.post({ type: 'tocWidth', width: state.settings.tocWidth });
    };
    resizer.addEventListener('pointermove', move);
    resizer.addEventListener('pointerup', up);
  });

  // ------------------------------------------------------------------ content interaction

  content.addEventListener('click', (e) => {
    const copyBtn = e.target.closest('.code-copy');
    if (copyBtn) {
      const code = copyBtn.parentElement.querySelector('code') || copyBtn.parentElement;
      const text = Array.from(code.childNodes).filter((n) => n !== copyBtn).map((n) => n.textContent).join('');
      writeClipboard(text).then(() => toast('Code copied'));
      return;
    }

    const cb = e.target.closest('li.task-list-item input[type=checkbox]');
    if (cb) {
      e.preventDefault(); // the re-rendered source is the single source of truth
      const li = cb.closest('li[data-line]');
      if (li) host.post({ type: 'toggleTask', line: +li.getAttribute('data-line') });
      return;
    }

    const a = e.target.closest('a[href]');
    if (a) {
      e.preventDefault();
      followLink(a);
    }
  });

  function followLink(a) {
    const raw = a.getAttribute('href');
    if (raw.startsWith('#')) {
      const id = decodeURIComponent(raw.slice(1));
      const target = document.getElementById(id) || document.getElementsByName(id)[0];
      if (target) revealElement(target);
      return;
    }
    host.post({ type: 'openLink', href: a.href });
  }

  // Double-click a block (outside of text selection gestures) with Ctrl to jump to its source.
  content.addEventListener('dblclick', (e) => {
    if (!e.ctrlKey) return;
    const range = linesForElement(e.target);
    if (range) host.post({ type: 'editSource', start: range.start, end: range.end });
  });

  function writeClipboard(text) {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      return navigator.clipboard.writeText(text).catch(() => fallbackCopy(text));
    }
    return Promise.resolve(fallbackCopy(text));
  }

  function fallbackCopy(text) {
    const ta = document.createElement('textarea');
    ta.value = text;
    ta.style.position = 'fixed';
    ta.style.opacity = '0';
    document.body.appendChild(ta);
    ta.select();
    document.execCommand('copy');
    ta.remove();
  }

  let toastTimer = 0;
  function toast(text) {
    const t = $('toast');
    t.textContent = text;
    t.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { t.hidden = true; }, 1400);
  }

  // ------------------------------------------------------------------ source line ranges

  function blockOf(node) {
    const el = node && (node.nodeType === 1 ? node : node.parentElement);
    return el ? el.closest('#content [data-line]') : null;
  }

  function isLeaf(el) {
    return !el.querySelector('[data-line]');
  }

  /** Lines that belong to an element's own text (excluding nested blocks where possible). */
  function ownRange(el) {
    const start = +el.getAttribute('data-line');
    let end = +el.getAttribute('data-line-end');
    if (!isLeaf(el)) {
      const firstChild = el.querySelector('[data-line]');
      const childStart = +firstChild.getAttribute('data-line');
      if (childStart > start) end = childStart - 1;
    }
    return { start, end: Math.max(start, end) };
  }

  function linesForElement(node) {
    const el = blockOf(node);
    if (!el) return null;
    return { start: +el.getAttribute('data-line'), end: +el.getAttribute('data-line-end') };
  }

  function linesForSelection() {
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return null;
    const range = sel.getRangeAt(0);
    if (!content.contains(range.commonAncestorContainer) && range.commonAncestorContainer !== content) return null;

    let start = Infinity, end = -Infinity;
    const take = (r) => { if (r) { start = Math.min(start, r.start); end = Math.max(end, r.end); } };

    const first = blockOf(range.startContainer);
    const last = blockOf(range.endContainer);
    if (first) take(ownRange(first));
    if (last) take(ownRange(last));
    for (const el of content.querySelectorAll('[data-line]')) {
      if (isLeaf(el) && range.intersectsNode(el)) take(ownRange(el));
    }
    return start === Infinity ? null : { start, end };
  }

  // ------------------------------------------------------------------ context menu

  document.addEventListener('contextmenu', (e) => {
    e.preventDefault();
    if (e.target.closest('#menu')) return;
    openMenu(e.clientX, e.clientY, buildMenu(e.target));
  });

  function buildMenu(target) {
    const sel = window.getSelection();
    const hasSelection = sel && !sel.isCollapsed && sel.toString().length > 0;
    const inContent = !!target.closest('#content');
    const lines = hasSelection ? linesForSelection() : (inContent ? linesForElement(target) : null);
    const link = target.closest('#content a[href]');
    const items = [];

    items.push({ label: 'Copy', key: 'Ctrl+C', disabled: !hasSelection, run: () => document.execCommand('copy') });
    items.push({
      label: hasSelection ? 'Copy as Markdown' : 'Copy block as Markdown',
      disabled: !lines,
      run: () => host.post({ type: 'copyMarkdown', start: lines.start, end: lines.end }),
    });
    items.push({ label: 'Select All', key: 'Ctrl+A', run: selectAll });
    if (link) {
      items.push('-');
      items.push({ label: 'Open Link', run: () => followLink(link) });
      items.push({ label: 'Copy Link Address', run: () => writeClipboard(link.href).then(() => toast('Link copied')) });
    }
    items.push('-');
    items.push({
      // Jumps to the source in the editor and highlights the lines behind it.
      label: hasSelection ? 'Edit Selection' : 'Edit This Block',
      key: 'Ctrl+E',
      disabled: !lines,
      bold: true,
      run: () => host.post({ type: 'editSource', start: lines.start, end: lines.end }),
    });
    items.push('-');
    items.push({ label: 'Find…', key: 'Ctrl+F', run: openFind });
    items.push({ label: 'Table of Contents', checked: state.settings.toc, run: () => host.post({ type: 'toc', show: !state.settings.toc }) });
    items.push('-');
    items.push({ label: 'Export as HTML…', run: () => host.post({ type: 'exportHtml' }) });
    items.push({ label: 'Export as PDF…', run: () => host.post({ type: 'exportPdf' }) });
    items.push({ label: 'Print…', run: () => host.post({ type: 'print' }) });
    items.push('-');
    items.push({ label: 'Settings…', run: () => host.post({ type: 'settings' }) });
    return items;
  }

  function openMenu(x, y, items) {
    menu.textContent = '';
    items.forEach((it) => {
      if (it === '-') {
        const sep = document.createElement('div');
        sep.className = 'sep';
        menu.appendChild(sep);
        return;
      }
      const row = document.createElement('div');
      row.className = 'item' + (it.disabled ? ' disabled' : '') + (it.checked ? ' checked' : '');
      row.setAttribute('role', 'menuitem');
      const label = document.createElement('span');
      label.textContent = it.label;
      if (it.bold) label.style.fontWeight = '600';
      row.appendChild(label);
      if (it.key) {
        const key = document.createElement('span');
        key.className = 'key';
        key.textContent = it.key;
        row.appendChild(key);
      }
      if (!it.disabled) {
        row.addEventListener('mousedown', (e) => e.preventDefault()); // keep the selection
        row.addEventListener('click', () => { closeMenu(); it.run(); });
      }
      menu.appendChild(row);
    });
    menu.hidden = false;
    const w = menu.offsetWidth, h = menu.offsetHeight;
    menu.style.left = Math.max(0, Math.min(x, window.innerWidth - w - 4)) + 'px';
    menu.style.top = Math.max(0, Math.min(y, window.innerHeight - h - 4)) + 'px';
  }

  function closeMenu() {
    menu.hidden = true;
  }

  document.addEventListener('mousedown', (e) => {
    if (!menu.hidden && !e.target.closest('#menu')) closeMenu();
  });
  window.addEventListener('blur', closeMenu);
  scroller.addEventListener('scroll', closeMenu, { passive: true });

  function selectAll() {
    const range = document.createRange();
    range.selectNodeContents(content);
    const sel = window.getSelection();
    sel.removeAllRanges();
    sel.addRange(range);
  }

  // ------------------------------------------------------------------ find

  const findbar = $('findbar');
  const findInput = $('find-input');

  function openFind() {
    findbar.hidden = false;
    const sel = window.getSelection().toString();
    if (sel && sel.length < 200 && !sel.includes('\n')) findInput.value = sel;
    findInput.focus();
    findInput.select();
    updateFindCount();
  }

  function closeFind() {
    findbar.hidden = true;
    scroller.focus();
  }

  function find(backwards) {
    const q = findInput.value;
    if (!q) return;
    // window.find searches the whole document; keep the query input from matching itself.
    findInput.blur();
    const found = window.find(q, false, backwards, true, false, false, false);
    if (!found) toast('No matches');
    findInput.focus({ preventScroll: true });
  }

  function updateFindCount() {
    const q = findInput.value.toLowerCase();
    if (!q) { $('find-count').textContent = ''; return; }
    const text = content.textContent.toLowerCase();
    let count = 0, i = 0;
    while ((i = text.indexOf(q, i)) !== -1 && count < 9999) { count++; i += q.length; }
    $('find-count').textContent = count === 1 ? '1 match' : count + ' matches';
  }

  findInput.addEventListener('input', updateFindCount);
  findInput.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') { e.preventDefault(); find(e.shiftKey); }
    else if (e.key === 'Escape') { e.preventDefault(); closeFind(); }
  });
  $('find-next').addEventListener('click', () => find(false));
  $('find-prev').addEventListener('click', () => find(true));
  $('find-close').addEventListener('click', closeFind);

  // ------------------------------------------------------------------ keyboard

  document.addEventListener('keydown', (e) => {
    const inInput = e.target.matches && e.target.matches('input, textarea');
    if (e.ctrlKey && !e.altKey && e.key.toLowerCase() === 'f') {
      e.preventDefault();
      openFind();
    } else if (e.ctrlKey && !e.altKey && e.key.toLowerCase() === 'a' && !inInput) {
      e.preventDefault();
      selectAll();
    } else if (e.ctrlKey && !e.altKey && e.key.toLowerCase() === 'e') {
      e.preventDefault();
      const lines = linesForSelection();
      if (!lines) toast('Select some text to edit');
      else host.post({ type: 'editSource', start: lines.start, end: lines.end });
    } else if (e.key === 'F3') {
      e.preventDefault();
      if (findbar.hidden) openFind(); else find(e.shiftKey);
    } else if (e.key === 'Escape') {
      if (!menu.hidden) closeMenu();
      else if (!findbar.hidden) closeFind();
    }
  });

  // ------------------------------------------------------------------ export

  window.marklens = {
    exportBody() {
      const clone = content.cloneNode(true);
      clone.querySelectorAll('.code-copy').forEach((b) => b.remove());
      clone.querySelectorAll('[data-line]').forEach((el) => {
        el.removeAttribute('data-line');
        el.removeAttribute('data-line-end');
      });
      clone.querySelectorAll('li.task-list-item input[type=checkbox]').forEach((cb) => cb.setAttribute('disabled', ''));
      return clone.innerHTML;
    },
  };

  host.post({ type: 'ready' });
})();
