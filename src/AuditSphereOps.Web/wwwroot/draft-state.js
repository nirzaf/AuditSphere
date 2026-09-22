window.auditSphereExports = window.auditSphereExports || {};
window.auditSphereExports.downloadText = (filename, text, contentType = 'text/plain;charset=utf-8') => {
  const blob = new Blob([text], { type: contentType });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), 0);
};
window.auditSphereExports.downloadBytes = (filename, bytes, contentType) => {
  const blob = new Blob([bytes], { type: contentType });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), 0);
};

(() => {
  const root = window.auditSphereDrafts = window.auditSphereDrafts || {};
  const wired = new WeakSet();
  const boundaries = new Set();
  const memoryDrafts = new Map();
  const timers = new WeakMap();
  const restoring = new WeakSet();
  const dirtyControls = new WeakSet();
  let guidanceSequence = 0;

  const keyFor = boundary => `auditsphere:draft:v1:${encodeURIComponent(boundary.dataset.draftScope)}`;
  const stores = [
    { name: 'localStorage', label: 'locally', state: 'saved' },
    { name: 'sessionStorage', label: 'for this browser session', state: 'session-only' }
  ];
  const controlsFor = (boundary, includeDisabled = false) => [...boundary.querySelectorAll('input, select, textarea')]
    .filter(control =>
      control.dataset.draftField &&
      (includeDisabled || !control.disabled) &&
      control.type !== 'file' &&
      control.type !== 'password' &&
      !control.dataset.draftSkip);

  function statusFor(boundary) {
    let status = boundary.querySelector('[data-draft-status]');
    if (!status) {
      status = document.createElement('p');
      status.className = 'draft-status';
      status.dataset.draftStatus = '';
      status.setAttribute('role', 'status');
      status.setAttribute('aria-live', 'polite');
      boundary.append(status);
    }
    return status;
  }

  function setStatus(boundary, message, state) {
    const status = statusFor(boundary);
    status.textContent = message;
    status.dataset.draftState = state;
  }

  function read(control) {
    if (control.type === 'checkbox') return control.checked;
    if (control.type === 'radio') return control.checked ? control.value : null;
    return control.value;
  }

  function write(control, value) {
    if (control.type === 'checkbox') {
      control.checked = value === true || value === 'true';
    } else if (control.type === 'radio') {
      control.checked = value !== null && value !== undefined && String(control.value) === String(value);
    } else if (value !== undefined && value !== null) {
      control.value = String(value);
    }
  }

  function collect(boundary) {
    const fields = {};
    for (const control of controlsFor(boundary, true)) {
      const field = control.dataset.draftField;
      if (control.type === 'radio') {
        if (!(field in fields)) fields[field] = null;
        if (control.checked) fields[field] = control.value;
      } else {
        fields[field] = read(control);
      }
    }
    return { fields, savedAt: new Date().toISOString() };
  }

  function writeStored(store, key, draft) {
    try {
      window[store.name].setItem(key, JSON.stringify(draft));
      return true;
    } catch {
      return false;
    }
  }

  function readStored(store, key) {
    try {
      return { available: true, raw: window[store.name].getItem(key) };
    } catch {
      return { available: false, raw: null };
    }
  }

  function save(boundary) {
    const draft = collect(boundary);
    const key = keyFor(boundary);
    memoryDrafts.set(key, draft);
    for (const store of stores) {
      if (writeStored(store, key, draft)) {
        setStatus(boundary, `Draft saved ${store.label}.`, store.state);
        return;
      }
    }
    setStatus(boundary, 'Draft saved for this page session; browser storage is unavailable.', 'session-only');
  }

  function scheduleSave(boundary) {
    clearTimeout(timers.get(boundary));
    timers.set(boundary, setTimeout(() => save(boundary), 150));
  }

  function restore(boundary, attempt = 0) {
    const key = keyFor(boundary);
    let draft;
    let storageLabel;
    for (const store of stores) {
      const result = readStored(store, key);
      if (!result.raw) continue;
      try {
        draft = JSON.parse(result.raw);
        storageLabel = store.label;
        break;
      } catch {
        try { window[store.name].removeItem(key); } catch { /* try the next store */ }
      }
    }
    if (!draft) {
      draft = memoryDrafts.get(key);
      storageLabel = 'for this page session';
    }
    if (!draft) {
      setStatus(boundary, 'Draft autosave is ready.', 'ready');
      return;
    }
    restoring.add(boundary);
    let unresolved = false;
    for (const control of controlsFor(boundary, true)) {
      if (dirtyControls.has(control)) continue;
      const field = control.dataset.draftField;
      if (!(field in (draft.fields || {}))) continue;
      const value = draft.fields[field];
      write(control, value);
      if (control.tagName === 'SELECT' && control.value !== String(value ?? '')) unresolved = true;
      if (control.type !== 'radio' || control.checked) {
        control.dispatchEvent(new Event('change', { bubbles: true }));
        if (control.tagName !== 'SELECT') control.dispatchEvent(new Event('input', { bubbles: true }));
      }
    }
    restoring.delete(boundary);
    const savedAt = draft.savedAt ? new Date(draft.savedAt) : null;
    const savedLabel = savedAt && !Number.isNaN(savedAt.valueOf())
      ? ` (${savedAt.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })})` : '';
    setStatus(boundary, `Unsaved draft restored ${storageLabel}${savedLabel}.`, 'restored');
    if (unresolved && attempt < 5) setTimeout(() => restore(boundary, attempt + 1), 250);
  }

  function wire(boundary) {
    if (wired.has(boundary) || !boundary.dataset.draftScope) return;
    wired.add(boundary);
    boundaries.add(boundary);
    restore(boundary);
    setTimeout(() => restore(boundary, 1), 250);
    const listen = event => {
      const control = event.target?.closest?.('input, select, textarea');
      if (control && !restoring.has(boundary)) dirtyControls.add(control);
      if (!restoring.has(boundary)) scheduleSave(boundary);
    };
    boundary.addEventListener('input', listen);
    boundary.addEventListener('change', listen);
    boundary.addEventListener('invalid', event => {
      const control = event.target?.closest?.('input, select, textarea');
      if (control) control.setAttribute('aria-invalid', 'true');
      setStatus(boundary, 'Complete the required fields before continuing.', 'invalid');
    }, true);
    boundary.addEventListener('input', event => {
      const control = event.target?.closest?.('input, select, textarea');
      if (control?.validity?.valid) control.removeAttribute('aria-invalid');
    });
  }

  function init(container = document) {
    for (const boundary of boundaries) {
      if (!document.contains(boundary)) boundaries.delete(boundary);
    }
    discover(container);
    addTooltips(container);
    if (container.matches?.('[data-draft-scope]')) wire(container);
    container.querySelectorAll?.('[data-draft-scope]').forEach(wire);
  }

  function addTooltips(container) {
    container.querySelectorAll?.('button:not([title]), a.button:not([title]), a[class*="btn-"]:not([title]), .table-wrap a[href]:not([title])').forEach(button => {
      const text = (button.dataset.tooltip || button.getAttribute('aria-label') || button.textContent || '').replace(/\s+/g, ' ').trim();
      if (text) button.title = `Activate to ${text.toLowerCase()}.`;
    });
  }

  function labelTextFor(control) {
    const label = control.labels?.[0] || control.closest('label');
    if (!label) return '';
    const copy = label.cloneNode(true);
    copy.querySelectorAll('input, select, textarea, option').forEach(node => node.remove());
    return copy.textContent.replace(/\s+/g, ' ').replace(/\(optional\)/ig, '').replace(/\*/g, '').trim();
  }

  function fieldHintFor(control) {
    return labelTextFor(control) || control.getAttribute('aria-label') ||
      control.getAttribute('placeholder') || control.name || control.id || '';
  }

  function readableHint(value) {
    return value.replace(/[-_]+/g, ' ').replace(/\s+/g, ' ').trim().replace(/\b\w/g, letter => letter.toUpperCase());
  }

  function discover(container) {
    const candidates = [
      ...(container.matches?.('[data-draft-scope], .card') ? [container] : []),
      ...(container.querySelectorAll?.('[data-draft-scope], .card') || []),
      ...(container.querySelectorAll?.('form:not(.card)') || [])
    ];
    const candidateSet = new Set(candidates);
    const boundaries = [...new Set(candidates)].filter(boundary => {
      let parent = boundary.parentElement;
      while (parent) {
        if (candidateSet.has(parent)) return false;
        parent = parent.parentElement;
      }
      return true;
    });
    const generatedScopes = new Map();
    boundaries.forEach((boundary, boundaryIndex) => {
      const controls = [...boundary.querySelectorAll('input, select, textarea')];
      if (!controls.length) return;
      if (!boundary.dataset.draftScope) {
        const identifier = boundary.id || boundary.getAttribute('aria-labelledby') || boundary.querySelector('h2[id], h3[id], legend')?.id ||
          boundary.querySelector('h2, h3, legend')?.textContent?.trim() || `card-${boundaryIndex}`;
        const baseScope = `${location.pathname}:${identifier}`;
        const existingScopes = new Set([...document.querySelectorAll('[data-draft-scope]')]
          .filter(node => node !== boundary && (node.dataset.draftScope === baseScope || node.dataset.draftScope.startsWith(`${baseScope}:`)))
          .map(node => node.dataset.draftScope));
        let duplicateIndex = generatedScopes.get(baseScope) || 0;
        while (existingScopes.has(duplicateIndex === 0 ? baseScope : `${baseScope}:${duplicateIndex}`)) duplicateIndex++;
        generatedScopes.set(baseScope, duplicateIndex + 1);
        boundary.dataset.draftScope = duplicateIndex === 0 ? baseScope : `${baseScope}:${duplicateIndex}`;
      }
      let guidance = boundary.querySelector('[data-draft-guidance]');
      if (!guidance) {
        guidance = document.createElement('p');
        guidance.className = 'field-help';
        guidance.dataset.draftGuidance = '';
        guidance.id = `draft-guidance-${++guidanceSequence}`;
        guidance.textContent = 'Fields marked * are required. Fields marked optional can be left blank.';
        const firstControl = controls[0];
        const firstField = firstControl?.closest('.field, .form-group, label');
        if (firstField) firstField.before(guidance);
        else boundary.prepend(guidance);
      }
      controls.forEach((control, index) => {
        if (!control.dataset.draftField && control.type !== 'file') {
          control.dataset.draftField = control.name || control.id || `${control.tagName.toLowerCase()}-${index}`;
        }
        if (!control.name && control.dataset.draftField) control.name = control.dataset.draftField;
        if (!control.autocomplete && control.type !== 'file') control.autocomplete = 'off';
        const label = control.labels?.[0] || control.closest('label');
        const text = fieldHintFor(control);
        if (!control.title && text) {
          const action = control.tagName === 'SELECT' ? 'Choose' :
            control.type === 'checkbox' || control.type === 'radio' ? 'Set' :
            control.readOnly ? 'View' : 'Enter';
          control.title = `${action} ${readableHint(text).replace(/:$/, '')}.`;
        }
        const required = control.required || control.dataset.required === 'true';
        const optional = !required && (control.dataset.optional === 'true' ||
          /\boptional\b/i.test(label?.textContent || '') ||
          (control.type !== 'file' && !control.readOnly && !control.disabled));
        if (required) {
          label?.classList.add('required-label');
          control.closest('.field, .form-group')?.classList.add('required-field');
          control.setAttribute('aria-required', 'true');
          control.dataset.fieldState = 'required';
        } else if (optional) {
          label?.classList.add('optional-label');
          control.closest('.field, .form-group')?.classList.add('optional-field');
          control.dataset.fieldState = 'optional';
        }
        if (guidance.id) {
          const describedBy = new Set((control.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean));
          describedBy.add(guidance.id);
          control.setAttribute('aria-describedby', [...describedBy].join(' '));
        }
      });
    });
  }

  root.init = init;
  root.saveAll = () => {
    for (const boundary of [...boundaries]) {
      if (!document.contains(boundary)) {
        boundaries.delete(boundary);
        continue;
      }
      clearTimeout(timers.get(boundary));
      save(boundary);
    }
  };
  root.clear = scope => {
    const key = `auditsphere:draft:v1:${encodeURIComponent(scope)}`;
    memoryDrafts.delete(key);
    for (const store of stores) {
      try { window[store.name].removeItem(key); } catch { /* storage may be unavailable */ }
    }
    const escapedScope = window.CSS?.escape ? CSS.escape(scope) : scope.replace(/(["\\])/g, '\\$1');
    document.querySelectorAll(`[data-draft-scope="${escapedScope}"]`).forEach(boundary => {
      setStatus(boundary, 'Saved draft cleared after successful submission.', 'ready');
    });
  };

  document.addEventListener('DOMContentLoaded', () => {
    init();
    setTimeout(init, 250);
  });
  document.addEventListener('enhancedload', () => {
    init();
    setTimeout(init, 250);
  });
  window.addEventListener('beforeunload', () => root.saveAll());
  window.addEventListener('pagehide', () => root.saveAll());
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden') root.saveAll();
  });
  new MutationObserver(() => init()).observe(document.body, { childList: true, subtree: true });
})();
