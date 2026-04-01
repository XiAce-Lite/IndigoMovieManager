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

    var callback = global[callbackName];
    if (typeof callback === "function") {
      callback(payload);
    }
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
      return postRequest("update", { startIndex: startIndex, count: count });
    },

    getInfo: function (movieId) {
      return postRequest("getInfo", { movieId: movieId });
    },

    getInfos: function (movieIds) {
      return postRequest("getInfos", { movieIds: movieIds });
    },

    focusThum: function (movieId) {
      return postRequest("focusThum", { movieId: movieId });
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
