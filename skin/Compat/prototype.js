(function (global) {
  "use strict";

  // WhiteBrowser スキンが prototype.js を参照しても 404 で落ちないための最小互換。
  // 実 API は必要になった時点で段階的に追加する。
  global.Prototype = global.Prototype || { Version: "compat-stub" };

  if (typeof global.$ !== "function") {
    global.$ = function (id) {
      return global.document.getElementById(id);
    };
  }
})(window);
