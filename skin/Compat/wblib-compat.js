(function (global) {
  "use strict";

  // WhiteBrowser の wb.* 互換を段階的に載せるための最小ランタイム。
  // 旧 skin が頼る callback と alias を、JS 側の薄い shim で吸収する。
  var sequence = 0;
  var pending = new Map();
  var defaultThumbLimit = 200;
  global.__immCompatErrors = global.__immCompatErrors || [];
  var runtimeState = {
    focusedId: null,
    selectedIds: new Set(),
    dbName: "default.wb",
    skinName: "",
    findInfo: {
      find: "",
      result: 0,
      total: 0,
      sort: [""],
      filter: [],
      where: ""
    },
    findInfoRequestSerial: 0,
    findInfoEpoch: 0,
    focusRequestSerial: 0,
    focusEpoch: 0,
    focusSelectionActionSerial: 0,
    selectedRequestSerial: 0,
    selectedEpoch: 0,
    movieInfoCache: Object.create(null),
    visibleItemsCache: [],
    relationCache: Object.create(null),
    profileCache: Object.create(null),
    virtualFileCache: Object.create(null),
    skinEntered: false,
    skinLeft: false,
    allowMultiSelect: false,
    scrollElementId: "view",
    currentCallbackContext: null,
    seamlessScrollMode: 0,
    seamlessLoading: false,
    seamlessRenderedCount: 0,
    seamlessTotalCount: 0,
    seamlessRequestedCount: 0,
    seamlessLastBatchCount: 0,
    seamlessExhausted: false,
    seamlessPumpScheduled: false,
    seamlessScrollHandler: null,
    seamlessAttachedScrollElement: null,
    skinEnterPrefetchPromise: null,
    skinEnterDispatching: false,
    skinEnterRequestedUpdate: false
  };

  if (typeof global.addEventListener === "function") {
    global.addEventListener("error", function (event) {
      try {
        global.__immCompatErrors.push({
          type: "error",
          message: event && event.message ? event.message : "",
          filename: event && event.filename ? event.filename : "",
          lineno: event && event.lineno ? event.lineno : 0
        });
      } catch (_error) {
        // 例外回収自体で UI を止めない。
      }
    });

    global.addEventListener("unhandledrejection", function (event) {
      try {
        var reason = event && event.reason ? String(event.reason) : "";
        global.__immCompatErrors.push({
          type: "unhandledrejection",
          message: reason
        });
      } catch (_error) {
        // 例外回収自体で UI を止めない。
      }
    });
  }

  if (typeof Array.prototype.flatten !== "function") {
    Array.prototype.flatten = function () {
      var result = [];

      function append(items) {
        for (var index = 0; index < items.length; index += 1) {
          var item = items[index];
          if (Array.isArray(item)) {
            append(item);
          } else {
            result.push(item);
          }
        }
      }

      append(this);
      return result;
    };
  }

  if (typeof Array.prototype.uniq !== "function") {
    Array.prototype.uniq = function () {
      var seen = new Set();
      var result = [];
      for (var index = 0; index < this.length; index += 1) {
        var item = this[index];
        if (seen.has(item)) {
          continue;
        }

        seen.add(item);
        result.push(item);
      }

      return result;
    };
  }

  if (typeof Array.prototype.each !== "function") {
    Array.prototype.each = function (iterator) {
      if (typeof iterator !== "function") {
        return this;
      }

      for (var index = 0; index < this.length; index += 1) {
        iterator(this[index], index);
      }

      return this;
    };
  }

  if (typeof global.$F !== "function") {
    global.$F = function (elementOrId) {
      if (!global.document) {
        return "";
      }

      var element = typeof elementOrId === "string"
        ? global.document.getElementById(elementOrId)
        : elementOrId;
      return element && element.value !== undefined ? String(element.value) : "";
    };
  }

  global.Form = global.Form || {};
  global.Form.Element = global.Form.Element || {};

  if (typeof global.Form.Element.setValue !== "function") {
    global.Form.Element.setValue = function (elementOrId, value) {
      if (!global.document) {
        return null;
      }

      var element = typeof elementOrId === "string"
        ? global.document.getElementById(elementOrId)
        : elementOrId;
      if (!element || element.value === undefined) {
        return element || null;
      }

      element.value = value == null ? "" : String(value);
      return element;
    };
  }

  if (typeof global.Form.Element.clear !== "function") {
    global.Form.Element.clear = function (elementOrId) {
      return global.Form.Element.setValue(elementOrId, "");
    };
  }

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
      if (runtimeState.skinEnterDispatching && method === "update") {
        runtimeState.skinEnterRequestedUpdate = true;
      }
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

  function createThenableObject(value, promise) {
    if (!value || (typeof value !== "object" && typeof value !== "function") || !promise) {
      return value;
    }

    try {
      Object.defineProperty(value, "then", {
        configurable: true,
        enumerable: false,
        writable: true,
        value: function (onFulfilled, onRejected) {
          return promise.then(onFulfilled, onRejected);
        }
      });
      Object.defineProperty(value, "catch", {
        configurable: true,
        enumerable: false,
        writable: true,
        value: function (onRejected) {
          return promise.catch(onRejected);
        }
      });
      Object.defineProperty(value, "finally", {
        configurable: true,
        enumerable: false,
        writable: true,
        value: function (onFinally) {
          return promise.finally(onFinally);
        }
      });
    } catch (_error) {
      // defineProperty を拒否する host でも、同期値として使えることを優先する。
    }

    return value;
  }

  function createThenableNumber(value, promise) {
    if (!Number.isFinite(value) || value <= 0) {
      return value || 0;
    }

    return createThenableObject(new Number(value), promise);
  }

  function cloneFindInfoSnapshot(source) {
    var base = source && typeof source === "object" ? source : runtimeState.findInfo;
    return {
      find: base && base.find !== undefined ? base.find : "",
      result: Number(base && base.result) || 0,
      total: Number(base && base.total) || 0,
      sort: Array.isArray(base && base.sort) && base.sort.length > 0 ? base.sort.slice() : [""],
      filter: Array.isArray(base && base.filter) ? base.filter.slice() : [],
      where: base && base.where !== undefined ? String(base.where || "") : ""
    };
  }

  function updateFindInfoCache(payload, options) {
    if (!payload || typeof payload !== "object") {
      return;
    }

    runtimeState.findInfo = cloneFindInfoSnapshot(payload);
    if (!options || options.bumpEpoch !== false) {
      runtimeState.findInfoEpoch += 1;
    }
  }

  function readCachedProfileValue(key, fallbackValue) {
    if (key && Object.prototype.hasOwnProperty.call(runtimeState.profileCache, key)) {
      return runtimeState.profileCache[key];
    }

    return fallbackValue !== undefined ? fallbackValue : "";
  }

  function requestProfileValue(key) {
    return postRequest("getProfile", { key: key }).then(function (value) {
      if (key) {
        runtimeState.profileCache[key] = value;
      }

      return value;
    });
  }

  function requestDbNameValue() {
    return postRequest("getDBName", {}).then(function (value) {
      var normalizedValue = String(value || "").trim();
      if (normalizedValue) {
        runtimeState.dbName = normalizedValue;
      }

      return runtimeState.dbName;
    });
  }

  function requestSkinNameValue() {
    return postRequest("getSkinName", {}).then(function (value) {
      var normalizedValue = String(value || "").trim();
      if (normalizedValue) {
        runtimeState.skinName = normalizedValue;
      }

      return runtimeState.skinName;
    });
  }

  function requestFindInfoValue() {
    var requestEpoch = runtimeState.findInfoEpoch;
    // getFindInfo 同士が競合した時も、最後に開始した要求だけを cache へ反映する。
    runtimeState.findInfoRequestSerial += 1;
    var requestSerial = runtimeState.findInfoRequestSerial;
    return postRequest("getFindInfo", {}).then(function (payload) {
      if (
        requestEpoch !== runtimeState.findInfoEpoch ||
        requestSerial !== runtimeState.findInfoRequestSerial
      ) {
        return cloneFindInfoSnapshot(runtimeState.findInfo);
      }

      updateFindInfoCache(payload, { bumpEpoch: false });
      return cloneFindInfoSnapshot(runtimeState.findInfo);
    });
  }

  function requestFocusValue() {
    var requestEpoch = runtimeState.focusEpoch;
    runtimeState.focusRequestSerial += 1;
    var requestSerial = runtimeState.focusRequestSerial;
    return postRequest("getFocusThum", {}).then(function (payload) {
      if (
        requestEpoch !== runtimeState.focusEpoch ||
        requestSerial !== runtimeState.focusRequestSerial
      ) {
        return runtimeState.focusedId || 0;
      }

      var focusedMovieId = normalizeMovieId(
        payload && (payload.focusedMovieId || payload.movieId || payload.id || payload || 0)
      );
      if (focusedMovieId || focusedMovieId === 0) {
        runtimeState.focusedId = focusedMovieId || null;
      }

      return focusedMovieId;
    });
  }

  function bumpSelectedEpoch() {
    runtimeState.selectedEpoch += 1;
  }

  function bumpFocusEpoch() {
    runtimeState.focusEpoch += 1;
  }

  function beginFocusSelectionAction() {
    runtimeState.focusSelectionActionSerial += 1;
    return runtimeState.focusSelectionActionSerial;
  }

  function resolveDefaultThumbBaseClass(thumbElement) {
    if (!thumbElement) {
      return "thum";
    }

    if (thumbElement.dataset && thumbElement.dataset.immThumbBaseClass) {
      return thumbElement.dataset.immThumbBaseClass;
    }

    var className = String(thumbElement.className || "");
    var baseClassName = className.indexOf("cthum") >= 0 ? "cthum" : "thum";

    if (thumbElement.dataset) {
      thumbElement.dataset.immThumbBaseClass = baseClassName;
    }

    return baseClassName;
  }

  function replaceSelectedIds(selectedIds, synchronizeVisuals) {
    var normalizedSelectedIds = selectedIds
      .map(function (id) { return normalizeMovieId(id); })
      .filter(function (id) { return id > 0; });
    var nextSelectedIds = new Set(normalizedSelectedIds);
    var changed = nextSelectedIds.size !== runtimeState.selectedIds.size;

    if (!changed) {
      Array.from(runtimeState.selectedIds).forEach(function (selectedId) {
        if (!nextSelectedIds.has(selectedId)) {
          changed = true;
        }
      });
    }

    if (changed) {
      bumpSelectedEpoch();
    }

    if (!synchronizeVisuals) {
      runtimeState.selectedIds = nextSelectedIds;
      return Array.from(runtimeState.selectedIds);
    }

    Array.from(runtimeState.selectedIds).forEach(function (selectedId) {
      if (nextSelectedIds.has(selectedId)) {
        return;
      }

      runtimeState.selectedIds.delete(selectedId);
      safeInvokeCallback("onSetSelect", { __immCallArgs: [selectedId, false] });
    });

    normalizedSelectedIds.forEach(function (selectedId) {
      if (runtimeState.selectedIds.has(selectedId)) {
        return;
      }

      runtimeState.selectedIds.add(selectedId);
      safeInvokeCallback("onSetSelect", { __immCallArgs: [selectedId, true] });
    });

    return Array.from(runtimeState.selectedIds);
  }

  function requestSelectedValues(synchronizeVisuals, options) {
    var requestEpoch = runtimeState.selectedEpoch;
    var useInternalSyncGuard = !!(options && options.internalSyncGuard);
    var requestSerial = 0;
    if (useInternalSyncGuard) {
      // compat 内部の再同期同士が競合した時だけ、最後に開始した要求へ反映先を絞る。
      runtimeState.selectedRequestSerial += 1;
      requestSerial = runtimeState.selectedRequestSerial;
    }
    return postRequest("getSelectThums", {}).then(function (payload) {
      var selectedIds = [];
      if (Array.isArray(payload)) {
        selectedIds = payload;
      } else if (payload && Array.isArray(payload.ids)) {
        selectedIds = payload.ids;
      }

      if (useInternalSyncGuard) {
        if (
          requestEpoch !== runtimeState.selectedEpoch ||
          requestSerial !== runtimeState.selectedRequestSerial
        ) {
          return Array.from(runtimeState.selectedIds);
        }

        return replaceSelectedIds(selectedIds, !!synchronizeVisuals);
      }

      if (requestEpoch === runtimeState.selectedEpoch) {
        replaceSelectedIds(selectedIds, !!synchronizeVisuals);
      }

      return selectedIds
        .map(function (id) { return normalizeMovieId(id); })
        .filter(function (id) { return id > 0; });
    });
  }

  function requestSelectedValuesSnapshot() {
    return postRequest("getSelectThums", {}).then(function (payload) {
      var selectedIds = [];
      if (Array.isArray(payload)) {
        selectedIds = payload;
      } else if (payload && Array.isArray(payload.ids)) {
        selectedIds = payload.ids;
      }

      return selectedIds
        .map(function (id) { return normalizeMovieId(id); })
        .filter(function (id) { return id > 0; });
    });
  }

  function cloneMovieInfo(info) {
    if (!info || typeof info !== "object") {
      return null;
    }

    return Object.assign({}, info);
  }

  function cacheMovieInfo(info) {
    var normalizedMovieId = normalizeMovieId(
      info && (info.movieId || info.MovieId || info.id || 0)
    );
    if (!normalizedMovieId || !info || typeof info !== "object") {
      return;
    }

    runtimeState.movieInfoCache[String(normalizedMovieId)] = cloneMovieInfo(info);
  }

  function updateCachedMovieInfo(info) {
    var normalizedMovieId = normalizeMovieId(
      info && (info.movieId || info.MovieId || info.id || 0)
    );
    if (!normalizedMovieId || !info || typeof info !== "object") {
      return null;
    }

    var cacheKey = String(normalizedMovieId);
    var mergedInfo = Object.assign(
      {},
      cloneMovieInfo(runtimeState.movieInfoCache[cacheKey]) || {},
      cloneMovieInfo(info) || {}
    );
    runtimeState.movieInfoCache[cacheKey] = mergedInfo;

    if (Array.isArray(runtimeState.visibleItemsCache)) {
      for (var index = 0; index < runtimeState.visibleItemsCache.length; index += 1) {
        var visibleInfo = runtimeState.visibleItemsCache[index];
        var visibleMovieId = normalizeMovieId(
          visibleInfo && (visibleInfo.movieId || visibleInfo.MovieId || visibleInfo.id || 0)
        );
        if (visibleMovieId !== normalizedMovieId) {
          continue;
        }

        runtimeState.visibleItemsCache[index] = Object.assign(
          {},
          cloneMovieInfo(visibleInfo) || {},
          mergedInfo
        );
        break;
      }
    }

    return cloneMovieInfo(mergedInfo);
  }

  function cacheMovieInfos(infos) {
    if (!Array.isArray(infos)) {
      return;
    }

    for (var index = 0; index < infos.length; index += 1) {
      cacheMovieInfo(infos[index]);
    }
  }

  function syncSelectedIdsFromItems(infos, resetSelection) {
    if (!Array.isArray(infos)) {
      return;
    }

    var resolvedStates = infos
      .map(function (info) {
        var normalizedMovieId = normalizeMovieId(
          info && (info.movieId || info.MovieId || info.id || 0)
        );
        var hasSelectedState = !!(
          info &&
          (info.select !== undefined || info.Select !== undefined)
        );

        if (!normalizedMovieId || !hasSelectedState) {
          return null;
        }

        return {
          movieId: normalizedMovieId,
          selected:
            Number(
              info.select !== undefined ? info.select : info.Select
            ) === 1
        };
      })
      .filter(function (state) {
        return !!state;
      });

    var selectedIds = resolvedStates
      .filter(function (state) {
        return state.selected;
      })
      .map(function (state) {
        return state.movieId;
      });

    if (resetSelection) {
      if (
        selectedIds.length !== runtimeState.selectedIds.size ||
        selectedIds.some(function (selectedId) {
          return !runtimeState.selectedIds.has(selectedId);
        })
      ) {
        bumpSelectedEpoch();
      }
      runtimeState.selectedIds = new Set(selectedIds);
      return;
    }

    resolvedStates.forEach(function (state) {
      var hadSelected = runtimeState.selectedIds.has(state.movieId);
      if (hadSelected !== state.selected) {
        bumpSelectedEpoch();
      }
      runtimeState.selectedIds.delete(state.movieId);
      if (state.selected) {
        runtimeState.selectedIds.add(state.movieId);
      }
    });
  }

  function applyDefaultSelectVisual(movieId, isSelected) {
    var normalizedMovieId = normalizeMovieId(movieId);
    if (!normalizedMovieId || !global.document || typeof global.document.getElementById !== "function") {
      return true;
    }

    var thumbElement = global.document.getElementById("thum" + normalizedMovieId);
    if (!thumbElement) {
      return true;
    }

    if (thumbElement.dataset) {
      thumbElement.dataset.immSelected = isSelected ? "1" : "0";
    }

    var thumbBaseClass = resolveDefaultThumbBaseClass(thumbElement);
    var isFocused = runtimeState.focusedId === normalizedMovieId;
    var usesCompactThumbClass = thumbBaseClass === "cthum";

    if (isSelected) {
      if (isFocused) {
        thumbElement.className = usesCompactThumbClass
          ? "cthum thum_select"
          : "thum_focus thum_select";
      } else {
        thumbElement.className = usesCompactThumbClass
          ? "cthum thum_select"
          : "thum_select";
      }
      return true;
    }

    if (isFocused) {
      thumbElement.className = usesCompactThumbClass ? "cthum_focus" : "thum_focus";
      return true;
    }

    thumbElement.className = thumbBaseClass;

    return true;
  }

  function applyDefaultFocusVisual(movieId, isFocused) {
    var normalizedMovieId = normalizeMovieId(movieId);
    if (!normalizedMovieId || !global.document || typeof global.document.getElementById !== "function") {
      return true;
    }

    var thumbElement = global.document.getElementById("thum" + normalizedMovieId);
    var imageElement = global.document.getElementById("img" + normalizedMovieId);
    var titleElement = global.document.getElementById("title" + normalizedMovieId);
    var vThumbElement = global.document.getElementById("vthum" + normalizedMovieId);
    var vImageElement = global.document.getElementById("vimg" + normalizedMovieId);
    var isSelected = false;

    if (thumbElement && thumbElement.dataset) {
      isSelected = thumbElement.dataset.immSelected === "1";
    }

    if (isFocused) {
      if (thumbElement) {
        if (resolveDefaultThumbBaseClass(thumbElement) === "cthum") {
          thumbElement.className = isSelected ? "cthum thum_select" : "cthum_focus";
        } else {
          thumbElement.className = isSelected ? "thum_focus thum_select" : "thum_focus";
        }
      }

      if (imageElement) {
        var focusedImageClassName = String(imageElement.className || "");
        if (focusedImageClassName.indexOf("cimg") >= 0) {
          imageElement.className = "cimg_focus";
        } else {
          imageElement.className = "img_focus";
        }
      }

      if (titleElement && String(titleElement.className || "").indexOf("title_") === 0) {
        titleElement.className = "title_focus";
      }

      if (vThumbElement && String(vThumbElement.className || "").indexOf("cindex") >= 0) {
        vThumbElement.className = "cindex_focus";
      }

      if (vImageElement && String(vImageElement.className || "").indexOf("cimg") >= 0) {
        vImageElement.className = "cimg_focus";
      }

      return true;
    }

    if (thumbElement) {
      var thumbBaseClass = resolveDefaultThumbBaseClass(thumbElement);
      if (thumbBaseClass === "cthum") {
        thumbElement.className = isSelected ? "cthum thum_select" : "cthum";
      } else {
        thumbElement.className = isSelected ? "thum_select" : "thum";
      }
    }

    if (imageElement) {
      var imageClassName = String(imageElement.className || "");
      if (imageClassName.indexOf("cimg") >= 0) {
        imageElement.className = "cimg";
      } else {
        imageElement.className = "img_thum";
      }
    }

    if (titleElement && String(titleElement.className || "").indexOf("title_") === 0) {
      titleElement.className = "title_thum";
    }

    if (vThumbElement && String(vThumbElement.className || "").indexOf("cindex") >= 0) {
      vThumbElement.className = "cindex";
    }

    if (vImageElement && String(vImageElement.className || "").indexOf("cimg") >= 0) {
      vImageElement.className = "cimg";
    }

    return true;
  }

  function cacheVisibleItems(infos) {
    runtimeState.visibleItemsCache = Array.isArray(infos)
      ? infos.map(function (info) {
        return cloneMovieInfo(info) || {};
      })
      : [];
    cacheMovieInfos(runtimeState.visibleItemsCache);
  }

  function requestMovieInfoValue(movieId) {
    var normalizedMovieId = normalizeMovieId(movieId);
    if (!normalizedMovieId) {
      return Promise.resolve(null);
    }

    return postRequest("getInfo", { movieId: normalizedMovieId }).then(function (payload) {
      cacheMovieInfo(payload);
      return cloneMovieInfo(runtimeState.movieInfoCache[String(normalizedMovieId)]);
    });
  }

  function requestVisibleItemsValue() {
    return requestFindInfoValue().then(function (findInfo) {
      var visibleCount = Math.max(0, Number(findInfo && findInfo.result) || 0);
      if (visibleCount < 1) {
        cacheVisibleItems([]);
        return [];
      }

      return postRequest("getInfos", { startIndex: 0, count: visibleCount }).then(function (payload) {
        var items = Array.isArray(payload) ? payload : buildUpdateItems(payload);
        cacheVisibleItems(items);
        return runtimeState.visibleItemsCache.slice();
      });
    });
  }

  function buildRelationCacheKey(title, limit) {
    return String(title || "") + "::" + String(normalizeRangeNumber(limit, 20));
  }

  function requestRelationValue(title, limit) {
    var normalizedLimit = normalizeRangeNumber(limit, 20);
    return postRequest("getRelation", { title: String(title || ""), limit: normalizedLimit }).then(function (payload) {
      var relationItems = Array.isArray(payload) ? payload.slice() : [];
      runtimeState.relationCache[buildRelationCacheKey(title, normalizedLimit)] = relationItems;
      return relationItems.slice();
    });
  }

  function prefetchExtensionContext() {
    return Promise.all([requestFocusValue(), requestSelectedValues()]).then(function (prefetchResults) {
      var focusedMovieId = normalizeMovieId(prefetchResults[0]);
      var selectedIds = Array.isArray(prefetchResults[1]) ? prefetchResults[1] : [];
      if (!focusedMovieId && selectedIds.length > 0) {
        runtimeState.focusedId = normalizeMovieId(selectedIds[0]);
      }

      if (!Array.isArray(selectedIds) || selectedIds.length < 1) {
        return true;
      }

      return Promise.all(
        selectedIds.map(function (selectedId) {
          return requestMovieInfoValue(selectedId);
        })
      ).then(function (infos) {
        return Promise.all(
          infos
            .filter(function (info) {
              return info && typeof info.title === "string" && info.title.length > 0;
            })
            .map(function (info) {
              return Promise.all([
                requestRelationValue(info.title, 20),
                requestRelationValue(info.title, 30)
              ]);
            })
        ).then(function () {
          return true;
        });
      });
    });
  }

  function prefetchCallbackContext(callbackName, payload) {
    if (callbackName === "onExtensionUpdated") {
      return prefetchExtensionContext();
    }

    if (
      callbackName === "onRegistedFile"
      && payload
      && typeof payload === "object"
      && Array.isArray(payload.__immCallArgs)
      && payload.__immCallArgs.length > 0
    ) {
      return requestMovieInfoValue(payload.__immCallArgs[0]).then(function () {
        return true;
      });
    }

    return Promise.resolve(true);
  }

  function prefetchSkinEnterContext() {
    if (runtimeState.skinEnterPrefetchPromise) {
      return runtimeState.skinEnterPrefetchPromise;
    }

    runtimeState.skinEnterPrefetchPromise = Promise.all([
      requestDbNameValue(),
      requestSkinNameValue(),
      requestVisibleItemsValue()
    ]).catch(function () {
      return true;
    }).finally(function () {
      runtimeState.skinEnterPrefetchPromise = null;
    });

    return runtimeState.skinEnterPrefetchPromise;
  }

  function waitForPromiseOrTimeout(promise, timeoutMs) {
    var normalizedTimeout = normalizeRangeNumber(timeoutMs, 0);
    if (!promise || normalizedTimeout < 1) {
      return Promise.resolve(true);
    }

    return Promise.race([
      promise.catch(function () {
        return true;
      }),
      new Promise(function (resolve) {
        global.setTimeout(function () {
          resolve(true);
        }, normalizedTimeout);
      })
    ]);
  }

  function safeInvokeCallback(callbackName, payload) {
    if (!callbackName) {
      return;
    }

    var callback = resolveCallback(callbackName);
    if (typeof callback === "function") {
      try {
        var result;
        var beforeViewChildCount = callbackName === "onUpdate" ? getViewChildCount() : -1;
        if (payload && typeof payload === "object" && Array.isArray(payload.__immCallArgs)) {
          result = callback.apply(global, payload.__immCallArgs);
        } else {
          result = callback(payload);
        }

        if (
          callbackName === "onUpdate"
          && Array.isArray(payload)
          && (result === false || result === undefined || result === null)
          && getViewChildCount() <= beforeViewChildCount
          && typeof resolveCallback("onCreateThum") === "function"
        ) {
          for (var updateIndex = 0; updateIndex < payload.length; updateIndex += 1) {
            safeInvokeCallback("onCreateThum", { __immCallArgs: [payload[updateIndex], 1] });
          }
          return;
        }

        // 旧 WB skin の onUpdateThum(id, src) は、今の recordKey 先頭契約だと空振りしやすい。
        // custom callback が更新を完了できなかった時だけ、慣例 DOM への既定差し替えを後段で試す。
        if (
          callbackName === "onUpdateThum"
          && (result === false || result === undefined || result === null)
        ) {
          applyDefaultThumbnailUpdate(payload);
        }
      } catch (error) {
        try {
          global.__immCompatErrors.push({
            type: "callback",
            callbackName: callbackName,
            message: error && error.message ? error.message : String(error || ""),
            stack: error && error.stack ? String(error.stack).slice(0, 400) : ""
          });
        } catch (_compatError) {
          // 監視用記録で処理を止めない。
        }

        if (callbackName === "onUpdateThum") {
          try {
            applyDefaultThumbnailUpdate(payload);
          } catch (thumbnailFallbackError) {
            if (global.console && typeof global.console.error === "function") {
              global.console.error(
                "WhiteBrowser compat thumbnail fallback failed:",
                thumbnailFallbackError
              );
            }
          }
        }

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
    return withResolvedCallbackOptions(promise, callbackName, selector, null);
  }

  function withResolvedCallbackOptions(promise, callbackName, selector, options) {
    return promise.then(function (payload) {
      ensureDefaultCallbacks();
      if (callbackName === "onUpdate" && payload && typeof payload === "object" && payload.findInfo) {
        // onClearAll で wb.getFindInfo() を読む旧 skin が多いため、
        // 応答に検索状態がある時は callback 前に cache へ反映する。
        updateFindInfoCache(payload.findInfo);
      }
      if (callbackName === "onUpdate" && options && options.resetView === true) {
        // query 切替や先頭再読込では、旧 DOM を先に落としてから一覧 callback を返す。
        handleClearAll();
      }
      var callbackPayload = typeof selector === "function" ? selector(payload) : payload;
      var previousCallbackContext = runtimeState.currentCallbackContext;
      runtimeState.currentCallbackContext = {
        callbackName: callbackName,
        resetView: !!(options && options.resetView === true),
        startIndex: payload && typeof payload === "object" && payload !== null && payload.startIndex !== undefined
          ? normalizeRangeNumber(payload.startIndex, 0)
          : 0
      };
      try {
        safeInvokeCallback(callbackName, callbackPayload);
      } finally {
        runtimeState.currentCallbackContext = previousCallbackContext;
      }

      if (callbackName === "onUpdate") {
        var updateItems = buildUpdateItems(payload);
        cacheMovieInfos(updateItems);
        syncSelectedIdsFromItems(
          updateItems,
          !!(options && options.resetView === true)
        );
        syncSeamlessScrollState(payload);
        return requestSelectedValues(true, { internalSyncGuard: true })
          .catch(function () {
            return Array.from(runtimeState.selectedIds);
          })
          .then(function () {
            return payload;
          });
      }

      return payload;
    });
  }

  function resolveResetViewFlag(startIndex) {
    var normalized = Number(startIndex);
    if (!Number.isFinite(normalized)) {
      normalized = 0;
    }

    return normalized <= 0;
  }

  function resolveThumbLimit() {
    var candidate = Number(global.g_thumbs_limit || global.wb.defaultThumbLimit || defaultThumbLimit);
    return Number.isFinite(candidate) && candidate > 0 ? candidate : defaultThumbLimit;
  }

  function normalizeRangeNumber(value, fallbackValue) {
    var numericValue = Number(value);
    if (!Number.isFinite(numericValue)) {
      return fallbackValue;
    }

    return Math.max(0, Math.floor(numericValue));
  }

  function buildRangePayload(startIndex, count, extraPayload) {
    var payload = extraPayload && typeof extraPayload === "object"
      ? Object.assign({}, extraPayload)
      : {};
    payload.startIndex = normalizeRangeNumber(startIndex, 0);
    payload.count = normalizeRangeNumber(count, resolveThumbLimit());
    return payload;
  }

  function normalizeRangePayloadObject(payload) {
    if (!payload || typeof payload !== "object") {
      return {};
    }

    var normalizedPayload = Object.assign({}, payload);
    if (
      Object.prototype.hasOwnProperty.call(normalizedPayload, "startIndex") ||
      Object.prototype.hasOwnProperty.call(normalizedPayload, "count")
    ) {
      normalizedPayload.startIndex = normalizeRangeNumber(normalizedPayload.startIndex, 0);
      normalizedPayload.count = normalizeRangeNumber(normalizedPayload.count, resolveThumbLimit());
    }

    return normalizedPayload;
  }

  function createVirtualFileStorageKey(path) {
    return "imm-wb-compat-file::" + String(path || "");
  }

  function readVirtualFileLines(path) {
    var normalizedPath = String(path || "");
    if (!normalizedPath) {
      return [];
    }

    if (Object.prototype.hasOwnProperty.call(runtimeState.virtualFileCache, normalizedPath)) {
      return runtimeState.virtualFileCache[normalizedPath].slice();
    }

    try {
      if (global.localStorage) {
        var serialized = global.localStorage.getItem(createVirtualFileStorageKey(normalizedPath));
        if (serialized) {
          var storedLines = JSON.parse(serialized);
          if (Array.isArray(storedLines)) {
            runtimeState.virtualFileCache[normalizedPath] = storedLines.slice();
            return storedLines.slice();
          }
        }
      }
    } catch (_error) {
      // localStorage が使えない host でもメモリ内 cache だけで継続する。
    }

    return [];
  }

  function writeVirtualFileLines(path, content) {
    var normalizedPath = String(path || "");
    if (!normalizedPath) {
      return 0;
    }

    var lines;
    if (Array.isArray(content)) {
      lines = content.map(function (line) { return String(line || ""); });
    } else if (content === undefined || content === null || content === "") {
      lines = [];
    } else {
      lines = String(content).split(/\r\n|\n|\r/);
    }

    runtimeState.virtualFileCache[normalizedPath] = lines.slice();
    try {
      if (global.localStorage) {
        global.localStorage.setItem(
          createVirtualFileStorageKey(normalizedPath),
          JSON.stringify(lines)
        );
      }
    } catch (_error) {
      // 永続化に失敗しても同一 page 中の互換 cache は残す。
    }

    return 1;
  }

  function resolveLegacyGetInfosCount(startIndex, endIndex) {
    var normalizedStart = normalizeRangeNumber(startIndex, 0);
    var numericEnd = Number(endIndex);
    if (!Number.isFinite(numericEnd) || numericEnd < 0) {
      return -1;
    }

    var normalizedEnd = Math.max(normalizedStart, Math.floor(numericEnd));
    return normalizedEnd - normalizedStart + 1;
  }

  function sliceVisibleItems(startIndex, endIndex) {
    var normalizedStart = normalizeRangeNumber(startIndex, 0);
    if (!Array.isArray(runtimeState.visibleItemsCache) || runtimeState.visibleItemsCache.length < 1) {
      return [];
    }

    var count = resolveLegacyGetInfosCount(normalizedStart, endIndex);
    if (count < 0) {
      return runtimeState.visibleItemsCache.slice(normalizedStart);
    }

    return runtimeState.visibleItemsCache.slice(normalizedStart, normalizedStart + count);
  }

  function getViewChildCount() {
    var view = resolveViewElement();
    if (!view || !view.children) {
      return 0;
    }

    return Number(view.children.length || 0);
  }

  function resolveViewElement() {
    if (!global.document || typeof global.document.getElementById !== "function") {
      return null;
    }

    var existingView = global.document.getElementById("view");
    if (existingView) {
      return existingView;
    }

    if (!global.document.body || typeof global.document.createElement !== "function") {
      return null;
    }

    var fallbackView = global.document.createElement("div");
    fallbackView.id = "view";
    fallbackView.setAttribute("data-imm-generated-view", "true");
    fallbackView.style.display = "block";
    fallbackView.style.padding = "8px";
    fallbackView.style.boxSizing = "border-box";
    global.document.body.appendChild(fallbackView);
    return fallbackView;
  }

  function readConfigValue(key, fallbackValue) {
    if (!global.document || typeof global.document.getElementById !== "function") {
      return fallbackValue;
    }

    var config = global.document.getElementById("config");
    if (!config || typeof config.textContent !== "string") {
      return fallbackValue;
    }

    var matcher = new RegExp(key.replace(/[.*+?^${}()|[\]\\]/g, "\\$&") + "\\s*:\\s*([^;]+);", "i");
    var matched = matcher.exec(config.textContent);
    return matched && matched[1] ? matched[1].trim() : fallbackValue;
  }

  function installLegacyIdGlobals() {
    if (!global.document || typeof global.document.querySelectorAll !== "function") {
      return;
    }

    var nodes = global.document.querySelectorAll("[id]");
    for (var index = 0; index < nodes.length; index += 1) {
      var node = nodes[index];
      if (!node || !node.id) {
        continue;
      }

      if (typeof global[node.id] === "undefined") {
        global[node.id] = node;
      }
    }
  }

  function initializeRuntimeConfig() {
    runtimeState.allowMultiSelect = Number(readConfigValue("multi-select", "0")) > 0;
    runtimeState.scrollElementId = readConfigValue("scroll-id", "view") || "view";
    // 既定 skin 群は config だけで seamless-scroll を宣言するため、ここで既定値を拾う。
    runtimeState.seamlessScrollMode = normalizeRangeNumber(
      readConfigValue("seamless-scroll", "0"),
      0
    );
  }

  function resetSeamlessScrollProgress() {
    runtimeState.seamlessLoading = false;
    runtimeState.seamlessRenderedCount = 0;
    runtimeState.seamlessTotalCount = 0;
    runtimeState.seamlessRequestedCount = resolveThumbLimit();
    runtimeState.seamlessLastBatchCount = 0;
    runtimeState.seamlessExhausted = false;
    runtimeState.seamlessPumpScheduled = false;
  }

  function detachSeamlessScrollListener() {
    if (
      runtimeState.seamlessAttachedScrollElement &&
      runtimeState.seamlessScrollHandler &&
      typeof runtimeState.seamlessAttachedScrollElement.removeEventListener === "function"
    ) {
      runtimeState.seamlessAttachedScrollElement.removeEventListener(
        "scroll",
        runtimeState.seamlessScrollHandler
      );
    }

    runtimeState.seamlessAttachedScrollElement = null;
    runtimeState.seamlessScrollHandler = null;
  }

  function attachSeamlessScrollListener() {
    var scrollElement = resolveScrollElement();
    if (!scrollElement || typeof scrollElement.addEventListener !== "function") {
      return;
    }

    if (runtimeState.seamlessAttachedScrollElement === scrollElement && runtimeState.seamlessScrollHandler) {
      return;
    }

    detachSeamlessScrollListener();
    runtimeState.seamlessScrollHandler = function () {
      scheduleSeamlessScrollPump();
    };
    scrollElement.addEventListener("scroll", runtimeState.seamlessScrollHandler);
    runtimeState.seamlessAttachedScrollElement = scrollElement;
  }

  function resolveUpdateStartIndex(payload) {
    if (payload && typeof payload === "object") {
      if (payload.startIndex !== undefined) {
        return normalizeRangeNumber(payload.startIndex, 0);
      }

      if (payload.StartIndex !== undefined) {
        return normalizeRangeNumber(payload.StartIndex, 0);
      }
    }

    return 0;
  }

  function resolveUpdateRequestedCount(payload) {
    if (payload && typeof payload === "object") {
      if (payload.requestedCount !== undefined) {
        return normalizeRangeNumber(payload.requestedCount, resolveThumbLimit());
      }

      if (payload.RequestedCount !== undefined) {
        return normalizeRangeNumber(payload.RequestedCount, resolveThumbLimit());
      }

      if (payload.count !== undefined) {
        return normalizeRangeNumber(payload.count, resolveThumbLimit());
      }

      if (payload.Count !== undefined) {
        return normalizeRangeNumber(payload.Count, resolveThumbLimit());
      }
    }

    return resolveThumbLimit();
  }

  function resolveUpdateTotalCount(payload, fallbackValue) {
    if (payload && typeof payload === "object") {
      if (payload.totalCount !== undefined) {
        return normalizeRangeNumber(payload.totalCount, fallbackValue);
      }

      if (payload.TotalCount !== undefined) {
        return normalizeRangeNumber(payload.TotalCount, fallbackValue);
      }
    }

    return fallbackValue;
  }

  function syncSeamlessScrollState(payload) {
    var items = buildUpdateItems(payload);
    var startIndex = resolveUpdateStartIndex(payload);
    var requestedCount = resolveUpdateRequestedCount(payload);
    var previousRenderedCount = runtimeState.seamlessRenderedCount;
    var renderedCount = startIndex <= 0
      ? items.length
      : Math.max(previousRenderedCount, startIndex + items.length);

    runtimeState.seamlessRequestedCount = Math.max(1, requestedCount);
    runtimeState.seamlessLastBatchCount = items.length;
    runtimeState.seamlessRenderedCount = renderedCount;
    runtimeState.seamlessTotalCount = Math.max(
      renderedCount,
      resolveUpdateTotalCount(payload, renderedCount)
    );

    if (startIndex <= 0) {
      runtimeState.seamlessExhausted = false;
      return;
    }

    // 追記要求で件数が増えなかった時は、空振りとして次の要求を止める。
    if (renderedCount <= previousRenderedCount) {
      runtimeState.seamlessExhausted = true;
      runtimeState.seamlessLastBatchCount = 0;
      runtimeState.seamlessTotalCount = renderedCount;
    }
  }

  function hasSeamlessScrollMoreItems() {
    if (runtimeState.seamlessExhausted) {
      return false;
    }

    if (runtimeState.seamlessTotalCount > 0) {
      return runtimeState.seamlessRenderedCount < runtimeState.seamlessTotalCount;
    }

    if (runtimeState.seamlessRenderedCount < 1) {
      return false;
    }

    return runtimeState.seamlessLastBatchCount >= Math.max(1, runtimeState.seamlessRequestedCount);
  }

  function isNearSeamlessScrollEnd() {
    var scrollElement = resolveScrollElement();
    if (!scrollElement) {
      return false;
    }

    var scrollTop = Number(scrollElement.scrollTop || 0);
    var clientHeight = Number(scrollElement.clientHeight || 0);
    var scrollHeight = Number(scrollElement.scrollHeight || 0);
    if (scrollHeight <= 0 || clientHeight <= 0) {
      return false;
    }

    return scrollTop + clientHeight + 96 >= scrollHeight;
  }

  function invokeAppendUpdate(items, startIndex) {
    ensureDefaultCallbacks();
    var normalizedItems = Array.isArray(items) ? items : [];
    if (normalizedItems.length < 1) {
      return true;
    }

    if (typeof resolveCallback("onCreateThum") === "function") {
      for (var index = 0; index < normalizedItems.length; index += 1) {
        safeInvokeCallback("onCreateThum", { __immCallArgs: [normalizedItems[index], 1] });
      }
      return true;
    }

    var previousCallbackContext = runtimeState.currentCallbackContext;
    runtimeState.currentCallbackContext = {
      callbackName: "onUpdate",
      resetView: false,
      startIndex: normalizeRangeNumber(startIndex, 0)
    };
    try {
      safeInvokeCallback("onUpdate", normalizedItems);
    } finally {
      runtimeState.currentCallbackContext = previousCallbackContext;
    }

    return true;
  }

  function requestSeamlessScrollAppend() {
    var requestedCount = runtimeState.seamlessRequestedCount;
    if (
      runtimeState.seamlessScrollMode <= 0 ||
      runtimeState.seamlessLoading ||
      !runtimeState.skinEntered ||
      runtimeState.skinLeft ||
      !hasSeamlessScrollMoreItems() ||
      !isNearSeamlessScrollEnd()
    ) {
      return Promise.resolve(false);
    }

    if (runtimeState.seamlessTotalCount > runtimeState.seamlessRenderedCount) {
      requestedCount = Math.max(
        1,
        Math.min(
          requestedCount,
          runtimeState.seamlessTotalCount - runtimeState.seamlessRenderedCount
        )
      );
    }

    runtimeState.seamlessLoading = true;
    attachSeamlessScrollListener();
    return postRequest(
      "update",
      buildRangePayload(runtimeState.seamlessRenderedCount, requestedCount)
    )
      .then(function (payload) {
        syncSeamlessScrollState(payload);
        invokeAppendUpdate(buildUpdateItems(payload), resolveUpdateStartIndex(payload));
        scheduleSeamlessScrollPump();
        return true;
      })
      .catch(function (error) {
        if (global.console && typeof global.console.error === "function") {
          global.console.error("WhiteBrowser seamless scroll append failed:", error);
        }
        return false;
      })
      .then(function (succeeded) {
        runtimeState.seamlessLoading = false;
        return succeeded;
      });
  }

  function scheduleSeamlessScrollPump() {
    if (
      runtimeState.seamlessScrollMode <= 0 ||
      runtimeState.seamlessLoading ||
      runtimeState.seamlessPumpScheduled ||
      !runtimeState.skinEntered ||
      runtimeState.skinLeft
    ) {
      return;
    }

    runtimeState.seamlessPumpScheduled = true;
    var schedule = typeof global.requestAnimationFrame === "function"
      ? global.requestAnimationFrame.bind(global)
      : function (callback) { return global.setTimeout(callback, 0); };
    schedule(function () {
      runtimeState.seamlessPumpScheduled = false;
      requestSeamlessScrollAppend();
    });
  }

  function resolveScrollElement() {
    if (!global.document || typeof global.document.getElementById !== "function") {
      return null;
    }

    return global.document.getElementById(runtimeState.scrollElementId || "view") || resolveViewElement();
  }

  function resolveRelativeOffsetTop(viewItem, scrollElement) {
    var current = viewItem;
    var relativeTop = 0;

    while (current && current !== scrollElement) {
      relativeTop += Number(current.offsetTop || 0);
      current = current.offsetParent || null;
    }

    if (current === scrollElement) {
      return Math.max(0, relativeTop);
    }

    return Math.max(0, Number(viewItem && viewItem.offsetTop ? viewItem.offsetTop : 0));
  }

  function clearViewElement() {
    var view = resolveViewElement();
    if (!view) {
      return false;
    }

    view.innerHTML = "";
    return true;
  }

  function escapeHtml(text) {
    return String(text || "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function extractMovieIdFromRecordKey(recordKey) {
    var normalizedRecordKey = String(recordKey || "").trim();
    if (!normalizedRecordKey) {
      return 0;
    }

    var matched = /(\d+)$/.exec(normalizedRecordKey);
    if (!matched || matched.length < 2) {
      return 0;
    }

    return normalizeMovieId(matched[1]);
  }

  function resolveThumbnailUpdatePayload(recordKeyOrPayload, thumbUrl, thumbRevision, thumbSourceKind, sizeInfo) {
    if (recordKeyOrPayload && typeof recordKeyOrPayload === "object" && !Array.isArray(recordKeyOrPayload)) {
      return {
        recordKey: String(recordKeyOrPayload.recordKey || ""),
        movieId: normalizeMovieId(recordKeyOrPayload.movieId || recordKeyOrPayload.id || 0),
        thumbUrl: String(recordKeyOrPayload.thumbUrl || recordKeyOrPayload.thum || ""),
        thumbRevision: String(recordKeyOrPayload.thumbRevision || ""),
        thumbSourceKind: String(recordKeyOrPayload.thumbSourceKind || ""),
        sizeInfo: recordKeyOrPayload.sizeInfo || sizeInfo || null
      };
    }

    return {
      recordKey: String(recordKeyOrPayload || ""),
      movieId: 0,
      thumbUrl: String(thumbUrl || ""),
      thumbRevision: String(thumbRevision || ""),
      thumbSourceKind: String(thumbSourceKind || ""),
      sizeInfo: sizeInfo || null
    };
  }

  function collectThumbnailTargets(payload) {
    if (!global.document || typeof global.document.querySelectorAll !== "function") {
      return [];
    }

    var movieId = payload.movieId || extractMovieIdFromRecordKey(payload.recordKey);
    var targets = [];
    var seen = [];

    function pushTarget(element) {
      if (!element) {
        return;
      }

      for (var index = 0; index < seen.length; index += 1) {
        if (seen[index] === element) {
          return;
        }
      }

      seen.push(element);
      targets.push(element);
    }

    if (movieId > 0) {
      pushTarget(global.document.getElementById("img" + movieId));

      var thumbContainer = global.document.getElementById("thum" + movieId);
      if (thumbContainer && typeof thumbContainer.querySelectorAll === "function") {
        var nestedImages = thumbContainer.querySelectorAll("img");
        for (var nestedIndex = 0; nestedIndex < nestedImages.length; nestedIndex += 1) {
          pushTarget(nestedImages[nestedIndex]);
        }
      }

      var movieTargets = global.document.querySelectorAll(
        '[data-movie-id="' + String(movieId) + '"], [data-id="' + String(movieId) + '"]'
      );
      for (var movieIndex = 0; movieIndex < movieTargets.length; movieIndex += 1) {
        pushTarget(movieTargets[movieIndex]);
      }
    }

    if (payload.recordKey) {
      var recordTargets = global.document.querySelectorAll(
        '[data-record-key="' + payload.recordKey.replace(/"/g, '\\"') + '"]'
      );
      for (var recordIndex = 0; recordIndex < recordTargets.length; recordIndex += 1) {
        pushTarget(recordTargets[recordIndex]);
      }
    }

    return targets;
  }

  function applyDefaultThumbnailUpdate(recordKeyOrPayload, thumbUrl, thumbRevision, thumbSourceKind, sizeInfo) {
    var payload = resolveThumbnailUpdatePayload(
      recordKeyOrPayload,
      thumbUrl,
      thumbRevision,
      thumbSourceKind,
      sizeInfo
    );
    if (!payload.thumbUrl) {
      return false;
    }

    var targets = collectThumbnailTargets(payload);
    if (targets.length < 1) {
      return false;
    }

    for (var index = 0; index < targets.length; index += 1) {
      var target = targets[index];
      if (!target || !target.tagName) {
        continue;
      }

      if (String(target.tagName).toLowerCase() === "img") {
        target.setAttribute("src", payload.thumbUrl);
        target.removeAttribute("data-thumb-url");
        continue;
      }

      if (target.style) {
        target.style.backgroundImage = 'url("' + payload.thumbUrl.replace(/"/g, '\\"') + '")';
      }
    }

    return true;
  }

  function createDefaultThumbnailMarkup(movie, dir) {
    var view = resolveViewElement();
    if (!view || typeof view.insertAdjacentHTML !== "function") {
      return false;
    }

    var movieId = normalizeMovieId(movie && (movie.id || movie.movieId || movie.MovieId || 0));
    if (!movieId) {
      return false;
    }

    var thumbnailUrl = escapeHtml(movie && (movie.thum || movie.thumbUrl || movie.ThumbUrl || ""));
    var titleText = escapeHtml(movie && ((movie.title || movie.MovieName || movie.movieName || "") + (movie.ext || "")));
    var selectedClass = movie && Number(movie.select || 0) === 1 ? "thum_select" : "thum";
    var existStyle = movie && movie.exist === false ? ' style="filter:Gray"' : "";
    var html =
      '<div class="' + selectedClass + '" id="thum' + String(movieId) + '">' +
        '<img class="img_thum" id="img' + String(movieId) + '" src="' + thumbnailUrl + '"' + existStyle + '>' +
        '<div id="title' + String(movieId) + '">' + titleText + '</div>' +
      '</div>';

    if (dir < 0 && typeof view.insertAdjacentHTML === "function") {
      view.insertAdjacentHTML("afterbegin", html);
      return true;
    }

    view.insertAdjacentHTML("beforeend", html);
    return true;
  }

  function normalizeMovieId(value) {
    var numericValue = Number(value);
    return Number.isFinite(numericValue) ? numericValue : 0;
  }

  function htmlEncode(value) {
    var text = String(value || "");
    return text
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;")
      .replace(/'/g, "&#39;");
  }

  function htmlDecode(value) {
    var text = String(value || "");
    if (!global.document || typeof global.document.createElement !== "function") {
      return text
        .replace(/&nbsp;/gi, " ")
        .replace(/&lt;/gi, "<")
        .replace(/&gt;/gi, ">")
        .replace(/&quot;/gi, '"')
        .replace(/&#39;/gi, "'")
        .replace(/&amp;/gi, "&");
    }

    var textarea = global.document.createElement("textarea");
    textarea.innerHTML = text;
    return textarea.value;
  }

  function escapeSingleQuotedScriptString(value) {
    return String(value || "")
      .replace(/\\/g, "\\\\")
      .replace(/'/g, "\\'")
      .replace(/\r/g, "\\r")
      .replace(/\n/g, "\\n");
  }

  function buildDefaultTagMarkup(movieId, tags) {
    var normalizedMovieId = normalizeMovieId(movieId);
    if (!normalizedMovieId || !Array.isArray(tags)) {
      return "";
    }

    return tags
      .map(function (tag) {
        var normalizedTag = String(tag || "");
        var decodedTag = htmlDecode(normalizedTag);
        var scriptTag = escapeSingleQuotedScriptString(decodedTag);
        var displayTag = htmlEncode(normalizedTag);
        return (
          "<li><a href=\"javascript:wb.find('" + scriptTag + "')\">" +
          displayTag +
          "</a><a href=\"javascript:wb.removeTag(" +
          String(normalizedMovieId) +
          ",'" +
          scriptTag +
          "')\" class=\"a_remove\">[x]</a></li>"
        );
      })
      .join("");
  }

  function applyDefaultTagUpdate(movieId, tags) {
    var normalizedMovieId = normalizeMovieId(movieId);
    if (!normalizedMovieId || !global.document || typeof global.document.getElementById !== "function") {
      return false;
    }

    updateCachedMovieInfo({
      movieId: normalizedMovieId,
      id: normalizedMovieId,
      tags: Array.isArray(tags) ? tags.slice() : []
    });

    var tagElement = global.document.getElementById("tag" + String(normalizedMovieId));
    if (!tagElement) {
      return false;
    }

    tagElement.innerHTML = buildDefaultTagMarkup(normalizedMovieId, tags);
    return true;
  }

  function applyFocusState(movieId, isFocused) {
    var normalizedMovieId = normalizeMovieId(movieId);
    var previousFocusedId = runtimeState.focusedId;
    if (previousFocusedId && (!isFocused || previousFocusedId !== normalizedMovieId)) {
      safeInvokeCallback("onSetFocus", { __immCallArgs: [previousFocusedId, false] });
      applyDefaultFocusVisual(previousFocusedId, false);
    }

    runtimeState.focusedId = isFocused && normalizedMovieId ? normalizedMovieId : null;
    if (previousFocusedId !== runtimeState.focusedId) {
      bumpFocusEpoch();
    }
    if (runtimeState.focusedId && previousFocusedId !== runtimeState.focusedId) {
      safeInvokeCallback("onSetFocus", { __immCallArgs: [runtimeState.focusedId, true] });
    }
  }

  // host 応答を使って、WPF 側で動いた実フォーカスへ JS 状態を追従させる。
  function synchronizeFocusState(payload, fallbackMovieId) {
    var focusedMovieId = normalizeMovieId(
      payload && (payload.focusedMovieId || (payload.focused ? payload.movieId || payload.id || fallbackMovieId || 0 : 0))
    );

    if (focusedMovieId) {
      applyFocusState(focusedMovieId, true);
      return;
    }

    if (payload && Object.prototype.hasOwnProperty.call(payload, "focused")) {
      applyFocusState(runtimeState.focusedId || fallbackMovieId || 0, false);
    }
  }

  function resolveCurrentMovieIdFallback() {
    if (runtimeState.focusedId) {
      return runtimeState.focusedId;
    }

    var selectedIds = Array.from(runtimeState.selectedIds);
    return selectedIds.length > 0 ? selectedIds[0] : 0;
  }

  function isMovieIdLike(value) {
    if (typeof value === "number") {
      return Number.isFinite(value);
    }

    if (typeof value !== "string") {
      return false;
    }

    var trimmed = value.trim();
    if (!trimmed) {
      return false;
    }

    var numericValue = Number(trimmed);
    return Number.isFinite(numericValue);
  }

  function resolveMovieIdLike(value) {
    return isMovieIdLike(value) ? normalizeMovieId(value) : 0;
  }

  function normalizeTagMutationRequest(movieIdOrTag, tagOrMovieId) {
    var movieId = 0;
    var tagName = "";

    if (movieIdOrTag && typeof movieIdOrTag === "object" && !Array.isArray(movieIdOrTag)) {
      movieId =
        resolveMovieIdLike(movieIdOrTag.movieId) ||
        resolveMovieIdLike(movieIdOrTag.id) ||
        resolveCurrentMovieIdFallback();
      tagName = movieIdOrTag.tag || movieIdOrTag.tagName || movieIdOrTag.value || movieIdOrTag.name || "";
    } else if (
      tagOrMovieId !== undefined &&
      isMovieIdLike(movieIdOrTag) &&
      !isMovieIdLike(tagOrMovieId)
    ) {
      movieId = resolveMovieIdLike(movieIdOrTag);
      tagName = tagOrMovieId;
    } else if (
      movieIdOrTag !== undefined &&
      isMovieIdLike(tagOrMovieId) &&
      !isMovieIdLike(movieIdOrTag)
    ) {
      movieId = resolveMovieIdLike(tagOrMovieId);
      tagName = movieIdOrTag;
    } else {
      movieId = resolveCurrentMovieIdFallback();
      tagName = tagOrMovieId !== undefined ? tagOrMovieId : movieIdOrTag;
    }

    return {
      movieId: movieId,
      tag: typeof tagName === "string" ? tagName.trim() : String(tagName || "").trim()
    };
  }

  function handleTagMutationResult(payload, fallbackMovieId) {
    ensureDefaultCallbacks();
    var resolvedMovieId = payload && (payload.movieId || payload.id || fallbackMovieId || 0);
    var updatedInfo = payload && payload.item && typeof payload.item === "object"
      ? updateCachedMovieInfo(payload.item)
      : null;

    if (resolvedMovieId && Array.isArray(payload && payload.tags)) {
      updatedInfo = updateCachedMovieInfo({
        movieId: resolvedMovieId,
        id: resolvedMovieId,
        tags: payload.tags.slice(),
        Tags: payload.tags.slice()
      }) || updatedInfo;
    }

    if (updatedInfo && Array.isArray(updatedInfo.Tags) && !Array.isArray(updatedInfo.tags)) {
      updatedInfo.tags = updatedInfo.Tags.slice();
      updateCachedMovieInfo(updatedInfo);
    }

    if (resolvedMovieId) {
      synchronizeFocusState(payload, resolvedMovieId);

      if (payload && Object.prototype.hasOwnProperty.call(payload, "selected")) {
        applySelectionState(resolvedMovieId, !!payload.selected);
      }
    }

    if (payload && payload.changed) {
      safeInvokeCallback(
        "onModifyTags",
        payload && payload.item ? Object.assign({}, payload.item, payload) : payload
      );
    }

    return payload;
  }

  function applySelectionState(movieId, isSelected) {
    var normalizedMovieId = normalizeMovieId(movieId);
    if (!normalizedMovieId) {
      return;
    }

    if (isSelected && !runtimeState.allowMultiSelect) {
      Array.from(runtimeState.selectedIds).forEach(function (selectedId) {
        if (selectedId === normalizedMovieId) {
          return;
        }

        bumpSelectedEpoch();
        runtimeState.selectedIds.delete(selectedId);
        safeInvokeCallback("onSetSelect", { __immCallArgs: [selectedId, false] });
      });
    }

    if (isSelected) {
      if (!runtimeState.selectedIds.has(normalizedMovieId)) {
        bumpSelectedEpoch();
        runtimeState.selectedIds.add(normalizedMovieId);
        safeInvokeCallback("onSetSelect", { __immCallArgs: [normalizedMovieId, true] });
      }
      return;
    }

    if (runtimeState.selectedIds.delete(normalizedMovieId)) {
      bumpSelectedEpoch();
      safeInvokeCallback("onSetSelect", { __immCallArgs: [normalizedMovieId, false] });
    }
  }

  function clearFocusAndSelectionState() {
    if (runtimeState.focusedId) {
      safeInvokeCallback("onSetFocus", { __immCallArgs: [runtimeState.focusedId, false] });
      applyDefaultFocusVisual(runtimeState.focusedId, false);
      runtimeState.focusedId = null;
      bumpFocusEpoch();
    }

    if (runtimeState.selectedIds.size < 1) {
      return;
    }

    // clearAll / skinLeave 中の callback から wb.getSelectThums() を読んでも、
    // すでに空になった host 同期 state を返せるよう先に cache を落とす。
    var selectedIdsToClear = Array.from(runtimeState.selectedIds);
    bumpSelectedEpoch();
    runtimeState.selectedIds.clear();
    selectedIdsToClear.forEach(function (selectedId) {
      safeInvokeCallback("onSetSelect", { __immCallArgs: [selectedId, false] });
    });
  }

  function handleClearAll() {
    ensureDefaultCallbacks();
    resetSeamlessScrollProgress();
    clearFocusAndSelectionState();
    safeInvokeCallback("onClearAll");
    return true;
  }

  function handleSkinLeave() {
    if (runtimeState.skinLeft) {
      return true;
    }

    ensureDefaultCallbacks();
    runtimeState.skinLeft = true;
    detachSeamlessScrollListener();
    resetSeamlessScrollProgress();
    handleClearAll();
    safeInvokeCallback("onSkinLeave");
    runtimeState.skinEntered = false;
    return true;
  }

  function buildUpdateItems(payload) {
    if (payload && Array.isArray(payload.items)) {
      return payload.items;
    }

    if (payload && Array.isArray(payload.Items)) {
      return payload.Items;
    }

    if (Array.isArray(payload)) {
      return payload;
    }

    return [];
  }

  function dispatchCompatCallback(callbackName, payload) {
    if (callbackName === "onExtensionUpdated" || callbackName === "onRegistedFile") {
      return prefetchCallbackContext(callbackName, payload).then(function () {
        safeInvokeCallback(callbackName, payload);
        return true;
      });
    }

    safeInvokeCallback(callbackName, payload);
    return Promise.resolve(true);
  }

  function ensureDefaultCallbacks() {
    if (!global.wb) {
      return;
    }

    if (typeof resolveCallback("onClearAll") !== "function") {
      global.wb.onClearAll = function () {
        return clearViewElement();
      };
    }

    if (typeof resolveCallback("onCreateThum") !== "function") {
      global.wb.onCreateThum = function (movie, dir) {
        // callback 未実装の外部 skin でも、まずはサムネ表示だけは通すための最小 fallback。
        return createDefaultThumbnailMarkup(movie || {}, Number(dir || 1));
      };
    }

    if (typeof resolveCallback("onUpdate") !== "function" && typeof resolveCallback("onCreateThum") === "function") {
      global.wb.onUpdate = function (movies) {
        var items = Array.isArray(movies) ? movies : [];
        if (!runtimeState.currentCallbackContext || runtimeState.currentCallbackContext.callbackName !== "onUpdate") {
          handleClearAll();
        }
        for (var i = 0; i < items.length; i += 1) {
          safeInvokeCallback("onCreateThum", { __immCallArgs: [items[i], 1] });
        }
        return true;
      };
    }

    if (typeof resolveCallback("onSetFocus") !== "function") {
      global.wb.onSetFocus = function (movieId, isFocused) {
        // focus callback 未実装の skin でも、最低限の focus 見た目だけは揃える。
        return applyDefaultFocusVisual(movieId, !!isFocused);
      };
    }

    if (typeof resolveCallback("onSetSelect") !== "function") {
      global.wb.onSetSelect = function (movieId, isSelected) {
        // 選択 callback 未実装の skin でも、最低限の選択見た目だけは揃える。
        return applyDefaultSelectVisual(movieId, !!isSelected);
      };
    }

    if (typeof resolveCallback("onSkinLeave") !== "function") {
      global.wb.onSkinLeave = function () {
        return true;
      };
    }

    if (typeof resolveCallback("onModifyTags") !== "function") {
      global.wb.onModifyTags = function (movieId, tags) {
        // 個別 callback を持たない skin でも、慣例的な tag コンテナは差し替える。
        return applyDefaultTagUpdate(movieId, tags);
      };
    }

    if (typeof resolveCallback("onUpdateThum") !== "function") {
      global.wb.onUpdateThum = function (recordKeyOrPayload, thumbUrl, thumbRevision, thumbSourceKind, sizeInfo) {
        // 個別 callback を持たない skin でも、慣例的な DOM id に対しては src 差し替えを試みる。
        return applyDefaultThumbnailUpdate(
          recordKeyOrPayload,
          thumbUrl,
          thumbRevision,
          thumbSourceKind,
          sizeInfo
        );
      };
    }

    if (typeof resolveCallback("onSkinEnter") !== "function") {
      global.wb.onSkinEnter = function () {
        if (typeof resolveCallback("onCreateThum") === "function" || typeof resolveCallback("onUpdate") === "function") {
          return global.wb.update(0, resolveThumbLimit());
        }

        return Promise.resolve(true);
      };
    }
  }

  function handleSkinEnter() {
    if (runtimeState.skinEntered) {
      return;
    }

    var hasCustomOnSkinEnter = typeof resolveCallback("onSkinEnter") === "function";
    initializeRuntimeConfig();
    runtimeState.skinEntered = true;
    runtimeState.skinLeft = false;
    if (runtimeState.seamlessScrollMode > 0) {
      attachSeamlessScrollListener();
    }
    ensureDefaultCallbacks();
    // 旧 skin は id 属性をそのまま global 参照する前提が多いため、IE 互換の最低限を補う。
    installLegacyIdGlobals();
    var invokeOnSkinEnter = function () {
      runtimeState.skinEnterDispatching = true;
      runtimeState.skinEnterRequestedUpdate = false;
      try {
        safeInvokeCallback("onSkinEnter");
      } finally {
        runtimeState.skinEnterDispatching = false;
      }

      if (
        !runtimeState.skinEnterRequestedUpdate
        && (typeof resolveCallback("onCreateThum") === "function" || typeof resolveCallback("onUpdate") === "function")
      ) {
        global.wb.update(0, resolveThumbLimit());
      }
    };

    if (hasCustomOnSkinEnter) {
      waitForPromiseOrTimeout(prefetchSkinEnterContext(), 250).finally(invokeOnSkinEnter);
      return;
    }

    invokeOnSkinEnter();
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
      return dispatchCompatCallback(callbackName, payload);
    },

    handleClearAll: function () {
      return handleClearAll();
    },

    handleSkinLeave: function () {
      return handleSkinLeave();
    }
  };

  global.wb = global.wb || {};
  global.wb.defaultThumbLimit = defaultThumbLimit;

  Object.assign(global.wb, {
    htmlEncode: htmlEncode,
    htmlDecode: htmlDecode,
    update: function (startIndex, count) {
      return withResolvedCallbackOptions(
        postRequest("update", buildRangePayload(startIndex, count)),
        "onUpdate",
        buildUpdateItems,
        { resetView: resolveResetViewFlag(startIndex) }
      );
    },

    find: function (keyword, startIndex, count) {
      return withResolvedCallbackOptions(
        postRequest("find", buildRangePayload(startIndex, count, { keyword: keyword })),
        "onUpdate",
        buildUpdateItems,
        { resetView: resolveResetViewFlag(startIndex) }
      );
    },

    sort: function (sortId, startIndex, count) {
      return withResolvedCallbackOptions(
        postRequest("sort", buildRangePayload(startIndex, count, { sortId: sortId })),
        "onUpdate",
        buildUpdateItems,
        { resetView: resolveResetViewFlag(startIndex) }
      );
    },

    addWhere: function (where, startIndex, count) {
      return withResolvedCallbackOptions(
        postRequest("addWhere", buildRangePayload(startIndex, count, { where: where })).then(function (payload) {
          // Search_table のように onClearAll で where を同期参照する skin 向けに、
          // where 更新だけは callback 前に最新 getFindInfo を温めてから返す。
          return requestFindInfoValue()
            .catch(function () {
              return cloneFindInfoSnapshot(runtimeState.findInfo);
            })
            .then(function () {
              return payload;
            });
        }),
        "onUpdate",
        buildUpdateItems,
        { resetView: resolveResetViewFlag(startIndex) }
      );
    },

    addOrder: function (order, override, startIndex, count) {
      return withResolvedCallbackOptions(
        postRequest("addOrder", buildRangePayload(startIndex, count, {
          order: order,
          override: override === undefined ? 0 : override
        })),
        "onUpdate",
        buildUpdateItems,
        { resetView: resolveResetViewFlag(startIndex) }
      );
    },

    addFilter: function (filter, startIndex, count) {
      return withResolvedCallbackOptions(
        postRequest("addFilter", buildRangePayload(startIndex, count, { filter: filter })),
        "onUpdate",
        buildUpdateItems,
        { resetView: resolveResetViewFlag(startIndex) }
      );
    },

    removeFilter: function (filter, startIndex, count) {
      return withResolvedCallbackOptions(
        postRequest("removeFilter", buildRangePayload(startIndex, count, { filter: filter })),
        "onUpdate",
        buildUpdateItems,
        { resetView: resolveResetViewFlag(startIndex) }
      );
    },

    clearFilter: function (startIndex, count) {
      return withResolvedCallbackOptions(
        postRequest("clearFilter", buildRangePayload(startIndex, count)),
        "onUpdate",
        buildUpdateItems,
        { resetView: resolveResetViewFlag(startIndex) }
      );
    },

    getInfo: function (movieId) {
      var normalizedMovieId = normalizeMovieId(movieId);
      var request = requestMovieInfoValue(normalizedMovieId);
      var cachedInfo = cloneMovieInfo(runtimeState.movieInfoCache[String(normalizedMovieId)]) || {};
      return createThenableObject(cachedInfo, request);
    },

    getInfos: function (movieIdsOrStartIndex, countOrPayload) {
      if (arguments.length >= 3 || Number(countOrPayload) < 0) {
        var legacyStartIndex = normalizeRangeNumber(movieIdsOrStartIndex, 0);
        var legacyEndIndex = countOrPayload;
        var cachedLegacyItems = sliceVisibleItems(legacyStartIndex, legacyEndIndex);
        var legacyRequest = requestVisibleItemsValue().then(function () {
          return sliceVisibleItems(legacyStartIndex, legacyEndIndex);
        });
        return createThenableObject(cachedLegacyItems, legacyRequest);
      }

      var payload = {};

      if (Array.isArray(movieIdsOrStartIndex)) {
        payload = { movieIds: movieIdsOrStartIndex };
      } else if (movieIdsOrStartIndex && typeof movieIdsOrStartIndex === "object") {
        payload = normalizeRangePayloadObject(movieIdsOrStartIndex);
      } else if (movieIdsOrStartIndex !== undefined || countOrPayload !== undefined) {
        payload = buildRangePayload(movieIdsOrStartIndex, countOrPayload);
      }

      return postRequest("getInfos", payload).then(function (responsePayload) {
        cacheMovieInfos(responsePayload);
        return responsePayload;
      });
    },

    getFindInfo: function () {
      var request = requestFindInfoValue();
      return createThenableObject(cloneFindInfoSnapshot(runtimeState.findInfo), request);
    },

    getFocusThum: function () {
      var request = requestFocusValue();
      return createThenableNumber(runtimeState.focusedId || 0, request);
    },

    getSelectThums: function () {
      // skin 側の await は host 応答をそのまま返し、内部再同期とは競合させない。
      var request = requestSelectedValuesSnapshot();
      return createThenableObject(Array.from(runtimeState.selectedIds), request);
    },

    getProfile: function (key, fallbackValue) {
      if (fallbackValue !== undefined) {
        var request = requestProfileValue(key);
        return createThenableObject(
          new String(String(readCachedProfileValue(key, fallbackValue))),
          request
        );
      }

      return requestProfileValue(key);
    },

    writeProfile: function (key, value) {
      if (key) {
        runtimeState.profileCache[key] = value;
      }

      return postRequest("writeProfile", { key: key, value: value });
    },

    changeSkin: function (skinName) {
      return postRequest("changeSkin", { skinName: skinName }).then(function (changed) {
        var normalizedSkinName = String(skinName || "").trim();
        // skin 切替成功直後の旧 page でも、互換 API が新しい skin 名を返せるよう cache を先に進める。
        if (changed && normalizedSkinName) {
          runtimeState.skinName = normalizedSkinName;
        }

        return changed;
      });
    },

    // focus / select は compat 側で状態遷移を畳み、callback 二重発火を防ぐ。
    focusThum: function (movieId) {
      var actionSerial = beginFocusSelectionAction();
      return postRequest("focusThum", { movieId: movieId }).then(function (payload) {
        if (actionSerial !== runtimeState.focusSelectionActionSerial) {
          return payload;
        }

        ensureDefaultCallbacks();
        var resolvedMovieId = payload && (payload.movieId || payload.id || movieId || 0);
        var selected = !!(payload && payload.selected);

        if (resolvedMovieId) {
          synchronizeFocusState(payload, resolvedMovieId);
          applySelectionState(resolvedMovieId, selected);
        }

        return requestSelectedValues(true, { internalSyncGuard: true })
          .catch(function () {
            return Array.from(runtimeState.selectedIds);
          })
          .then(function () {
            return payload;
          });
      });
    },

    selectThum: function (movieId, selected) {
      var actionSerial = beginFocusSelectionAction();
      return postRequest("selectThum", {
        movieId: movieId,
        selected: selected === undefined ? true : !!selected
      }).then(function (payload) {
        if (actionSerial !== runtimeState.focusSelectionActionSerial) {
          return payload;
        }

        ensureDefaultCallbacks();
        var resolvedMovieId = payload && (payload.movieId || payload.id || movieId || 0);
        var isSelected = !!(payload && (payload.selected !== undefined ? payload.selected : payload.focused));

        if (resolvedMovieId) {
          synchronizeFocusState(payload, resolvedMovieId);
          applySelectionState(resolvedMovieId, isSelected);
        }

        return requestSelectedValues(true, { internalSyncGuard: true })
          .catch(function () {
            return Array.from(runtimeState.selectedIds);
          })
          .then(function () {
            return payload;
          });
      });
    },

    addTag: function (movieIdOrTag, tagOrMovieId) {
      var request = normalizeTagMutationRequest(movieIdOrTag, tagOrMovieId);
      return postRequest("addTag", request).then(function (payload) {
        return handleTagMutationResult(payload, request.movieId);
      });
    },

    removeTag: function (movieIdOrTag, tagOrMovieId) {
      var request = normalizeTagMutationRequest(movieIdOrTag, tagOrMovieId);
      return postRequest("removeTag", request).then(function (payload) {
        return handleTagMutationResult(payload, request.movieId);
      });
    },

    flipTag: function (movieIdOrTag, tagOrMovieId) {
      var request = normalizeTagMutationRequest(movieIdOrTag, tagOrMovieId);
      return postRequest("flipTag", request).then(function (payload) {
        return handleTagMutationResult(payload, request.movieId);
      });
    },

    getRelation: function (title, limit) {
      var normalizedLimit = normalizeRangeNumber(limit, 20);
      var cacheKey = buildRelationCacheKey(title, normalizedLimit);
      var request = requestRelationValue(title, normalizedLimit);
      var cachedRelation = runtimeState.relationCache[cacheKey];
      return createThenableObject(Array.isArray(cachedRelation) ? cachedRelation.slice() : [], request);
    },

    getFileList: function () {
      // 旧 skin の初期化で使う最小互換。外部画像探索が無くても mv.thum で描画は継続できる。
      return [];
    },

    readFile: function (path) {
      return readVirtualFileLines(path);
    },

    writeFile: function (path, content) {
      return writeVirtualFileLines(path, content);
    },

    getWatchList: function () {
      // 旧拡張 skin のフォルダ tree では watch list が無くても tree 構築は継続できる。
      return [];
    },

    makeThum: function (imageId, movieId) {
      if (!global.document || typeof global.document.getElementById !== "function") {
        return 0;
      }

      var imageElement = global.document.getElementById(String(imageId || ""));
      if (!imageElement) {
        imageElement = global.document.getElementById("img" + String(normalizeMovieId(movieId)));
      }

      if (!imageElement) {
        return 0;
      }

      var nextThumbUrl = imageElement.getAttribute("data-thumb-url");
      if (!nextThumbUrl) {
        nextThumbUrl = imageElement.getAttribute("src") || "";
      }

      if (!nextThumbUrl) {
        return 0;
      }

      if ((imageElement.getAttribute("src") || "") !== nextThumbUrl) {
        imageElement.setAttribute("src", nextThumbUrl);
      }

      return 1;
    },

    thumSetting: function () {
      // 旧 skin の初期化で呼ばれる最小互換。
      // 実際のサムネ契約は mv.thum / makeThum 側で成立しているため、
      // ここでは例外を出さず初期化を前へ進めることを優先する。
      return 1;
    },

    getDBName: function () {
      return createThenableObject(new String(runtimeState.dbName || "default.wb"), requestDbNameValue());
    },

    getSkinName: function () {
      return createThenableObject(new String(runtimeState.skinName || ""), requestSkinNameValue());
    },

    getThumDir: function () {
      return postRequest("getThumDir", {});
    },

    scrollSetting: function (mode, scrollId) {
      if (typeof scrollId === "string" && scrollId.trim()) {
        runtimeState.scrollElementId = scrollId.trim();
      } else if (!runtimeState.scrollElementId) {
        runtimeState.scrollElementId = readConfigValue("scroll-id", "view") || "view";
      }

      runtimeState.seamlessScrollMode = normalizeRangeNumber(mode, 0);
      detachSeamlessScrollListener();
      resetSeamlessScrollProgress();
      if (runtimeState.seamlessScrollMode > 0) {
        attachSeamlessScrollListener();
      }

      if (Number(mode) > 0) {
        return global.wb.update(0, resolveThumbLimit());
      }

      return Promise.resolve(true);
    },

    scrollTo: function (movieId) {
      if (!global.document || typeof global.document.getElementById !== "function") {
        return Promise.resolve(false);
      }

      var movieKey = String(movieId || "");
      var viewItem =
        global.document.getElementById("thum" + movieKey) ||
        global.document.getElementById(movieKey) ||
        global.document.getElementById("img" + movieKey) ||
        global.document.getElementById("title" + movieKey);
      if (!viewItem) {
        return Promise.resolve(false);
      }

      var scrollElement = resolveScrollElement();
      if (scrollElement) {
        var relativeTop = resolveRelativeOffsetTop(viewItem, scrollElement);
        if (typeof scrollElement.scrollTo === "function") {
          scrollElement.scrollTo({ top: relativeTop, behavior: "auto" });
        } else {
          scrollElement.scrollTop = relativeTop;
        }

        return Promise.resolve(true);
      }

      if (typeof viewItem.scrollIntoView === "function") {
        viewItem.scrollIntoView({ block: "nearest", inline: "nearest" });
        return Promise.resolve(true);
      }

      return Promise.resolve(false);
    },

    trace: function (message) {
      return postRequest("trace", { message: message });
    }
  });

  if (global.document && global.document.readyState === "loading") {
    global.document.addEventListener("DOMContentLoaded", handleSkinEnter);
  } else {
    global.setTimeout(handleSkinEnter, 0);
  }

  global.addEventListener("beforeunload", function () {
    handleSkinLeave();
  });
})(window);
