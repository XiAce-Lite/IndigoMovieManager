(function (global) {
  "use strict";

  // WhiteBrowser スキンが prototype.js を参照しても 404 で落ちないための最小互換。
  // 実 API は必要になった時点で段階的に追加する。
  global.Prototype = global.Prototype || { Version: "compat-stub" };

  if (typeof global.$ !== "function") {
    global.$ = function (id) {
      if (!global.document) {
        return null;
      }

      var element = global.document.getElementById(id);
      if (element) {
        return element;
      }

      if (typeof global.document.getElementsByName === "function") {
        var namedElements = global.document.getElementsByName(id);
        if (namedElements && namedElements.length > 0) {
          return namedElements[0];
        }
      }

      return null;
    };
  }

  function resolveElement(elementOrId) {
    if (!elementOrId) {
      return null;
    }

    if (typeof elementOrId === "string" && global.document) {
      return global.document.getElementById(elementOrId);
    }

    return elementOrId;
  }

  if (!global.Element) {
    global.Element = {};
  }

  if (typeof global.Element.update !== "function") {
    global.Element.update = function (elementOrId, html) {
      var element = resolveElement(elementOrId);
      if (!element) {
        return null;
      }

      element.innerHTML = String(html || "");
      return element;
    };
  }

  if (typeof global.Element.remove !== "function") {
    global.Element.remove = function (elementOrId) {
      var element = resolveElement(elementOrId);
      if (!element || !element.parentNode) {
        return null;
      }

      element.parentNode.removeChild(element);
      return element;
    };
  }

  if (typeof String.prototype.escapeHTML !== "function") {
    String.prototype.escapeHTML = function () {
      return String(this)
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#39;");
    };
  }

  if (typeof String.prototype.unescapeHTML !== "function") {
    String.prototype.unescapeHTML = function () {
      return String(this)
        .replace(/&lt;/g, "<")
        .replace(/&gt;/g, ">")
        .replace(/&quot;/g, "\"")
        .replace(/&#39;/g, "'")
        .replace(/&amp;/g, "&");
    };
  }

  if (typeof Array.prototype.clone !== "function") {
    Array.prototype.clone = function () {
      return this.slice();
    };
  }

  if (typeof Array.prototype.compact !== "function") {
    Array.prototype.compact = function () {
      return this.filter(function (value) {
        return value != null && value !== "";
      });
    };
  }

  if (typeof Array.prototype.inspect !== "function") {
    Array.prototype.inspect = function () {
      return JSON.stringify(this);
    };
  }

  if (!global.Insertion) {
    // 旧 WB skin が `new Insertion.Top/Bottom(...)` を呼ぶので、必要最小限だけ吸収する。
    global.Insertion = {
      Top: function (element, html) {
        element = resolveElement(element);
        this.element = element || null;
        if (!element || typeof element.insertAdjacentHTML !== "function") {
          return;
        }

        element.insertAdjacentHTML("afterbegin", String(html || ""));
      },

      Bottom: function (element, html) {
        element = resolveElement(element);
        this.element = element || null;
        if (!element || typeof element.insertAdjacentHTML !== "function") {
          return;
        }

        element.insertAdjacentHTML("beforeend", String(html || ""));
      },

      Before: function (element, html) {
        element = resolveElement(element);
        this.element = element || null;
        if (!element || typeof element.insertAdjacentHTML !== "function") {
          return;
        }

        element.insertAdjacentHTML("beforebegin", String(html || ""));
      },

      After: function (element, html) {
        element = resolveElement(element);
        this.element = element || null;
        if (!element || typeof element.insertAdjacentHTML !== "function") {
          return;
        }

        element.insertAdjacentHTML("afterend", String(html || ""));
      }
    };
  }
})(window);
