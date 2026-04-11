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

  if (!global.Insertion) {
    // 旧 WB skin が `new Insertion.Top/Bottom(...)` を呼ぶので、必要最小限だけ吸収する。
    global.Insertion = {
      Top: function (element, html) {
        this.element = element || null;
        if (!element || typeof element.insertAdjacentHTML !== "function") {
          return;
        }

        element.insertAdjacentHTML("afterbegin", String(html || ""));
      },

      Bottom: function (element, html) {
        this.element = element || null;
        if (!element || typeof element.insertAdjacentHTML !== "function") {
          return;
        }

        element.insertAdjacentHTML("beforeend", String(html || ""));
      }
    };
  }
})(window);
