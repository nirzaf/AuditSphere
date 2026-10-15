/**
 * Line icons for the workspace sidebar. Drawn on a 24-unit grid and stroked in currentColor, so
 * they follow the link state (muted, hover, active) without a font or a third-party icon package.
 * Shapes are path data only, so the markup never needs raw HTML injection.
 */
export const NAV_ICON_PATHS = {
  activity: ['M3 12h4l2.5-6 4.5 12 2.5-6H21'],
  'bar-chart': ['M4 20h16', 'M7 16v-4', 'M12 16V8', 'M17 16v-7'],
  book: ['M6 3h13v15H6a2 2 0 0 0-2 2V5a2 2 0 0 1 2-2z'],
  'book-open': ['M2 4h6a4 4 0 0 1 4 4v12a3 3 0 0 0-3-3H2z', 'M22 4h-6a4 4 0 0 0-4 4v12a3 3 0 0 1 3-3h7z'],
  briefcase: ['M3 7h18v12H3z', 'M9 7V4.5h6V7'],
  calculator: ['M5 3h14v18H5z', 'M8 7h8v3H8z', 'M8 14h.01M12 14h.01M16 14h.01M8 17h.01M12 17h.01M16 17h.01'],
  calendar: ['M4 5h16v15H4z', 'M4 10h16', 'M8 3v4', 'M16 3v4'],
  'check-square': ['M4 4h16v16H4z', 'M8 12.5l2.7 2.7L16 9.5'],
  'clipboard-check': ['M9 4H7a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V6a2 2 0 0 0-2-2h-2', 'M9 3h6v4H9z', 'M9 13l2 2 4-4'],
  clock: ['M3 12a9 9 0 1 0 18 0 9 9 0 1 0-18 0', 'M12 7v5l3 2'],
  cloud: ['M7 18.5h10a4 4 0 0 0 .5-8 5.5 5.5 0 0 0-10.5 1.5A3.5 3.5 0 0 0 7 18.5z'],
  'currency-exchange': ['M4 8h13', 'M14 4l4 4-4 4', 'M20 16H7', 'M10 12l-4 4 4 4'],
  'file-text': ['M14 3H6a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V8z', 'M14 3v5h5', 'M8 13h8', 'M8 17h5'],
  'git-branch': ['M6 3v12', 'M18 9a9 9 0 0 1-9 9', 'M15 6a3 3 0 1 0 6 0 3 3 0 1 0-6 0', 'M3 18a3 3 0 1 0 6 0 3 3 0 1 0-6 0'],
  globe: ['M12 3a9 9 0 1 0 0 18 9 9 0 1 0 0-18z', 'M3 12h18', 'M12 3c2.5 2.6 3.5 5.6 3.5 9s-1 6.4-3.5 9c-2.5-2.6-3.5-5.6-3.5-9S9.5 5.6 12 3z'],
  grid: ['M4 4h7v7H4z', 'M13 4h7v7h-7z', 'M4 13h7v7H4z', 'M13 13h7v7h-7z'],
  history: ['M3 12a9 9 0 1 0 3-6.7', 'M3 4v5h5', 'M12 7v5l3 2'],
  layers: ['M12 2 2 7l10 5 10-5-10-5z', 'M2 17l10 5 10-5', 'M2 12l10 5 10-5'],
  'list-checks': ['M10 6h11', 'M10 12h11', 'M10 18h11', 'M3 6l1.5 1.5L7 5', 'M3 12l1.5 1.5L7 11', 'M3 18l1.5 1.5L7 17'],
  'plus-minus': ['M4 7h7', 'M7.5 3.5v7', 'M13 17h7'],
  receipt: ['M6 3h12v18l-2-1.5-2 1.5-2-1.5-2 1.5-2-1.5L6 21z', 'M9 8h6', 'M9 12h6'],
  'refresh-cw': ['M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8', 'M3 3v5h5', 'M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16', 'M16 16h5v5'],
  search: ['M10.5 18a7.5 7.5 0 1 0 0-15 7.5 7.5 0 0 0 0 15z', 'M21 21l-4.6-4.6'],
  shield: ['M12 3 5 6v5.5c0 4.6 3 8.4 7 9.5 4-1.1 7-4.9 7-9.5V6z'],
  sliders: ['M4 7h9', 'M17 7h3', 'M4 17h3', 'M11 17h9', 'M13 7a2 2 0 1 0 4 0 2 2 0 1 0-4 0', 'M7 17a2 2 0 1 0 4 0 2 2 0 1 0-4 0'],
  'trending-up': ['M3 17l6-6 4 4 8-8', 'M15 7h6v6'],
  user: ['M12 4a4 4 0 1 0 0 8 4 4 0 1 0 0-8z', 'M4.5 21c0-4 3.4-6.5 7.5-6.5s7.5 2.5 7.5 6.5'],
  'user-plus': ['M9.5 8a3.5 3.5 0 1 1-7 0 3.5 3.5 0 1 1 7 0', 'M2.5 20c0-3.3 2.4-5.5 6.5-5.5', 'M18 8v6', 'M15 11h6'],
  users: ['M5.5 8a3.5 3.5 0 1 0 7 0 3.5 3.5 0 1 0-7 0', 'M3 20c0-3.3 2.7-5.5 6-5.5s6 2.2 6 5.5', 'M15.5 4.8a3.5 3.5 0 0 1 0 6.9', 'M16 14.2c2.4.5 4 2.5 4 5.3'],
} as const;

export type NavIconName = keyof typeof NAV_ICON_PATHS;
