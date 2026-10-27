# Practice workspace visual foundation

**Status:** CURRENT

**Purpose:** The shared visual rules for AuditSphere's staff workspace, so every screen reads as one professional practice-management product. Implemented in `src/AuditSphereOps.Ui/src/styles.scss` (tokens and shared patterns), `app/app.scss` (shell) and `app/core/ui.ts` (page header, state and status components). Presentation only: these rules never change routes, contracts, commands, authorization or accessible names.

## Direction

Calm authority for regulated financial work: a navy shell, white work surfaces on a cool slate canvas, one blue action accent, semantic status colours, and dense but legible data. Minimal (Swiss) style with subtle motion. The system font stack is kept so no external font is downloaded.

## Tokens

All colours, lines, radii and elevation come from the `--as-*` custom properties on `:root`. Components reference tokens with a literal fallback so body-mounted overlays still render.

| Purpose | Token |
| --- | --- |
| Canvas, raised and sunken surfaces | `--as-canvas`, `--as-surface-raised`, `--as-surface-sunken` |
| Headings, secondary and muted text | `--as-ink`, `--as-ink-soft`, `--as-text-muted` |
| Lines and table header rule | `--as-line`, `--as-line-strong` |
| Links, focus, eyebrow accent | `--as-link`, `--as-focus`, `--as-accent-ink` |
| Radius: controls, cards, panels | `--as-radius-sm`, `--as-radius-md`, `--as-radius-lg` |
| Elevation | `--as-shadow-1`, `--as-shadow-2` |
| Content width | `--as-content-max` |

Text tokens keep at least 4.5:1 contrast on white and on the canvas; control borders keep 3:1.

## Shared patterns

| Pattern | Use | Markup |
| --- | --- | --- |
| Page header | Every page opens with it; projected content is page actions, shown right of the title | `<audit-page-header>` |
| Panel | A titled section of related content | `.panel` |
| Key figures | A row of tiles, each a short label above one tabular value | `section.portfolio-metrics > article > h2 + strong` |
| Filter strip | Search and filter controls, stacked label over control, actions aligned to the controls | `.filter-bar` (also `.lead-filters`, `.invoice-filters`, `.pipeline-filters`) |
| Segmented control | Views switched with `aria-pressed` buttons, or a record's section shortcuts | `nav.analytics-views`, `nav.queue-views`, `nav.invoice-sections`, `nav.proposal-sections` |
| Data table | Tables live in `.table-scroll` cards; numbers use `.number`; dates that must not wrap use `.nowrap` | `table`, `.table-scroll` |
| Amount strip | A record's headline amounts in one line above its sections | `.amount-strip` |
| Hint | Explanatory scope or basis text under a control or heading | `.hint` |
| Status | Persisted workflow states with a semantic tone | `<audit-status>` |
| Readable codes and times | Stored status, role and reason codes in plain words; stored instants as "27 Oct 2026, 03:16 UTC". Operator diagnostic pages that deliberately show exact state codes keep them. | `codeLabel` and `utcTime` pipes |

Material form fields use `appearance="outline" subscriptSizing="dynamic"` on each field at density -3. A global default provider is not used because importing the token eagerly grew the initial bundle.

## Rules

- Do not use CSS text transforms on headings, labels or column headers; screen readers and role-based tests read the rendered text.
- Keep 44 px minimum targets for navigation and primary actions; density applies to form fields and table cells, not to buttons.
- New summaries state their scope in a `.hint`; new tables declare a caption.
- Motion is limited to the one-time page entry and short hover transitions, and is removed under `prefers-reduced-motion`.
- At 375 px wide no page may scroll horizontally; wide tables scroll inside their card.
