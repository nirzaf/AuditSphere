'use strict';

// @angular/build's LMDB 3.5.6 native cache binding aborts on environment close
// on the supported macOS developer toolchain. Angular's built-in SQLite cache
// avoids that native binding without changing build output or adding a package.
if (process.platform === 'darwin') {
  process.env.NG_BUILD_CACHE_STORE = 'sqlite';
}

require('../node_modules/@angular/cli/bin/ng.js');
