(function (global) {
  "use strict";

  // WhiteBrowser の wb.* 互換を段階的に載せるための最小スケルトン。
  // いまは postMessage と callback dispatch の土台だけを持つ。
  var sequence = 0;
  var pending = new Map();

  function nextId() {
    sequence += 1;
    return "wb-" + sequence.toString(10);
  }

  function postRequest(method, payload) {
    return new Promise(function (resolve, reject) {
      if (!global.chrome || !global.chrome.webview) {
        reject(new Error("WebView2 bridge is not available."));
        return;
      }

      var id = nextId();
      pending.set(id, { resolve: resolve, reject: reject });
      global.chrome.webview.postMessage(
        JSON.stringify({
          id: id,
          method: method,
          payload: payload || {}
        })
      );
    });
  }

  function safeInvokeCallback(callbackName, payload) {
    if (!callbackName) {
      return;
    }

    var callback = resolveCallback(callbackName);
    if (typeof callback === "function") {
      try {
        callback(payload);
      } catch (error) {
        if (global.console && typeof global.console.error === "function") {
          global.console.error("WhiteBrowser compat callback failed:", callbackName, error);
        }
      }
    }
  }

  function resolveCallback(callbackName) {
    if (!callbackName) {
      return null;
    }

    if (callbackName.indexOf(".") >= 0) {
      return resolvePathCallback(callbackName);
    }

    if (global.wb && typeof global.wb[callbackName] === "function") {
      return global.wb[callbackName];
    }

    if (typeof global[callbackName] === "function") {
      return global[callbackName];
    }

    return null;
  }

  function resolvePathCallback(callbackPath) {
    var parts = callbackPath.split(".");
    var context = global;
    for (var i = 0; i < parts.length; i += 1) {
      if (!context) {
        return null;
      }

      context = context[parts[i]];
    }

    return typeof context === "function" ? context : null;
  }

  function withResolvedCallback(promise, callbackName, selector) {
    return promise.then(function (payload) {
      var callbackPayload = typeof selector === "function" ? selector(payload) : payload;
      safeInvokeCallback(callbackName, callbackPayload);
      return payload;
    });
  }

  global.__immWbCompat = {
    resolve: function (id, payload) {
      var item = pending.get(id);
      if (!item) {
        return;
      }

      pending.delete(id);
      item.resolve(payload);
    },

    reject: function (id, error) {
      var item = pending.get(id);
      if (!item) {
        return;
      }

      pending.delete(id);
      item.reject(new Error(error || "WhiteBrowser bridge rejected."));
    },

    dispatchCallback: function (callbackName, payload) {
      safeInvokeCallback(callbackName, payload);
    }
  };

  global.wb = {
    update: function (startIndex, count) {
      return withResolvedCallback(
        postRequest("update", { startIndex: startIndex, count: count }),
        "onUpdate",
        function (payload) {
          return payload && Array.isArray(payload.items) ? payload.items : payload;
        }
      );
    },

    find: function (keyword, startIndex, count) {
      return withResolvedCallback(
        postRequest("find", { keyword: keyword, startIndex: startIndex, count: count }),
        "onUpdate",
        function (payload) {
          return payload && Array.isArray(payload.items) ? payload.items : payload;
        }
      );
    },

    getInfo: function (movieId) {
      return postRequest("getInfo", { movieId: movieId });
    },

    getInfos: function (movieIds) {
      return postRequest("getInfos", { movieIds: movieIds });
    },

    focusThum: function (movieId) {
      return withResolvedCallback(
        postRequest("focusThum", { movieId: movieId }),
        "onSetFocus",
        function (payload) {
          return payload && payload.movie ? payload.movie : payload;
        }
      );
    },

    getSkinName: function () {
      return postRequest("getSkinName", {});
    },

    getDBName: function () {
      return postRequest("getDBName", {});
    },

    getThumDir: function () {
      return postRequest("getThumDir", {});
    },

    trace: function (message) {
      return postRequest("trace", { message: message });
    }
  };
})(window);
