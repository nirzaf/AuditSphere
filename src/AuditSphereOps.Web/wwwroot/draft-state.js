(() => {
  const root = window.auditSphereDrafts = window.auditSphereDrafts || {};
  const wired = new WeakSet();
  const timers = new WeakMap();
  const restoring = new WeakSet();
  const dirtyControls = new WeakSet();
  let guidanceSequence = 0;

  const keyFor = boundary => `auditsphere:draft:v1:${encodeURIComponent(boundary.dataset.draftScope)}`;
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
    if (control.type === 'checkbox' || control.type === 'radio') return control.checked;
    return control.value;
  }

  function write(control, value) {
    if (control.type === 'checkbox' || control.type === 'radio') {
      control.checked = Boolean(value);
    } else if (value !== undefined && value !== null) {
      control.value = String(value);
    }
  }

  function collect(boundary) {
    const fields = {};
    for (const control of controlsFor(boundary)) fields[control.dataset.draftField] = read(control);
    return { fields, savedAt: new Date().toISOString() };
  }

  function save(boundary) {
    try {
      localStorage.setItem(keyFor(boundary), JSON.stringify(collect(boundary)));
      setStatus(boundary, 'Draft saved locally.', 'saved');
    } catch {
      setStatus(boundary, 'Local draft saving is unavailable; keep this page open until the action completes.', 'unavailable');
    }
  }

  function scheduleSave(boundary) {
    clearTimeout(timers.get(boundary));
    timers.set(boundary, setTimeout(() => save(boundary), 150));
  }

  function restore(boundary, attempt = 0) {
    let raw;
    try { raw = localStorage.getItem(keyFor(boundary)); } catch {
      setStatus(boundary, 'Local draft saving is unavailable; keep this page open until the action completes.', 'unavailable');
      return;
    }
    if (!raw) {
      setStatus(boundary, 'Draft autosave is ready.', 'ready');
      return;
    }

    let draft;
    try { draft = JSON.parse(raw); } catch { return; }
    restoring.add(boundary);
    let unresolved = false;
    for (const control of controlsFor(boundary, true)) {
      if (dirtyControls.has(control)) continue;
      const field = control.dataset.draftField;
      if (!(field in (draft.fields || {}))) continue;
      const value = draft.fields[field];
      write(control, value);
      if (control.tagName === 'SELECT' && control.value !== String(value)) unresolved = true;
      control.dispatchEvent(new Event(control.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true }));
    }
    restoring.delete(boundary);
    setStatus(boundary, 'Unsaved draft restored locally.', 'restored');
    if (unresolved && attempt < 5) setTimeout(() => restore(boundary, attempt + 1), 250);
  }

  function wire(boundary) {
    if (wired.has(boundary) || !boundary.dataset.draftScope) return;
    wired.add(boundary);
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
    window.addEventListener('beforeunload', () => {
      clearTimeout(timers.get(boundary));
      save(boundary);
    });
  }

  function init(container = document) {
    discover(container);
    addTooltips(container);
    if (container.matches?.('[data-draft-scope]')) wire(container);
    container.querySelectorAll?.('[data-draft-scope]').forEach(wire);
  }

  function addTooltips(container) {
    container.querySelectorAll?.('button:not([title]), a.button:not([title]), a[class*="btn-"]:not([title])').forEach(button => {
      const text = (button.dataset.tooltip || button.getAttribute('aria-label') || button.textContent || '').replace(/\s+/g, ' ').trim();
      if (text) button.title = `Activate to ${text.toLowerCase()}.`;
    });
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
    boundaries.forEach((boundary, boundaryIndex) => {
      const controls = [...boundary.querySelectorAll('input, select, textarea')];
      if (!controls.length) return;
      if (!boundary.dataset.draftScope) {
        const identifier = boundary.getAttribute('aria-labelledby') || boundary.querySelector('h2[id], h3[id], legend')?.id ||
          boundary.querySelector('h2, h3, legend')?.textContent?.trim() || `card-${boundaryIndex}`;
        boundary.dataset.draftScope = `${location.pathname}:${identifier}`;
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
        const label = control.labels?.[0] || control.closest('label');
        if (label) {
          const text = label.textContent.replace(/\s+/g, ' ').replace(/\(optional\)/ig, '').replace(/\*/g, '').trim();
          if (!control.title && text) {
            const action = control.tagName === 'SELECT' ? 'Choose' :
              control.type === 'checkbox' || control.type === 'radio' ? 'Set' :
              control.readOnly ? 'View' : 'Enter';
            control.title = `${action} ${text.replace(/:$/, '')}.`;
          }
          if (control.required || control.dataset.required === 'true') {
            label.classList.add('required-label');
            control.setAttribute('aria-required', 'true');
          }
          if (control.dataset.optional === 'true' || /\boptional\b/i.test(label.textContent)) {
            label.classList.add('optional-label');
          }
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
  root.clear = scope => {
    try { localStorage.removeItem(`auditsphere:draft:v1:${encodeURIComponent(scope)}`); } catch { return; }
    document.querySelectorAll(`[data-draft-scope="${CSS.escape(scope)}"]`).forEach(boundary => {
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
  new MutationObserver(() => init()).observe(document.body, { childList: true, subtree: true });
})();
