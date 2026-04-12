(function (global) {
  "use strict";

  // WhiteBrowser の wb.* 互換を段階的に載せるための最小ランタイム。
  // 旧 skin が頼る callback と alias を、JS 側の薄い shim で吸収する。
  var sequence = 0;
  var pending = new Map();
  var defaultThumbLimit = 200;
  var runtimeState = {
    focusedId: null,
    selectedIds: new Set(),
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
    seamlessAttachedScrollElement: null
  };

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
        if (payload && typeof payload === "object" && Array.isArray(payload.__immCallArgs)) {
          callback.apply(global, payload.__immCallArgs);
          return;
        }

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
    return withResolvedCallbackOptions(promise, callbackName, selector, null);
  }

  function withResolvedCallbackOptions(promise, callbackName, selector, options) {
    return promise.then(function (payload) {
      ensureDefaultCallbacks();
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
        syncSeamlessScrollState(payload);
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

  function resolveViewElement() {
    if (!global.document || typeof global.document.getElementById !== "function") {
      return null;
    }

    return global.document.getElementById("view");
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

  function applyFocusState(movieId, isFocused) {
    var normalizedMovieId = normalizeMovieId(movieId);
    var previousFocusedId = runtimeState.focusedId;
    if (previousFocusedId && (!isFocused || previousFocusedId !== normalizedMovieId)) {
      safeInvokeCallback("onSetFocus", { __immCallArgs: [previousFocusedId, false] });
    }

    runtimeState.focusedId = isFocused && normalizedMovieId ? normalizedMovieId : null;
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

        runtimeState.selectedIds.delete(selectedId);
        safeInvokeCallback("onSetSelect", { __immCallArgs: [selectedId, false] });
      });
    }

    if (isSelected) {
      if (!runtimeState.selectedIds.has(normalizedMovieId)) {
        runtimeState.selectedIds.add(normalizedMovieId);
        safeInvokeCallback("onSetSelect", { __immCallArgs: [normalizedMovieId, true] });
      }
      return;
    }

    if (runtimeState.selectedIds.delete(normalizedMovieId)) {
      safeInvokeCallback("onSetSelect", { __immCallArgs: [normalizedMovieId, false] });
    }
  }

  function clearFocusAndSelectionState() {
    if (runtimeState.focusedId) {
      safeInvokeCallback("onSetFocus", { __immCallArgs: [runtimeState.focusedId, false] });
      runtimeState.focusedId = null;
    }

    if (runtimeState.selectedIds.size < 1) {
      return;
    }

    Array.from(runtimeState.selectedIds).forEach(function (selectedId) {
      safeInvokeCallback("onSetSelect", { __immCallArgs: [selectedId, false] });
    });
    runtimeState.selectedIds.clear();
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
      global.wb.onSetFocus = function () {
        return true;
      };
    }

    if (typeof resolveCallback("onSetSelect") !== "function") {
      global.wb.onSetSelect = function () {
        return true;
      };
    }

    if (typeof resolveCallback("onSkinLeave") !== "function") {
      global.wb.onSkinLeave = function () {
        return true;
      };
    }

    if (typeof resolveCallback("onModifyTags") !== "function") {
      global.wb.onModifyTags = function () {
        return true;
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

    initializeRuntimeConfig();
    runtimeState.skinEntered = true;
    runtimeState.skinLeft = false;
    if (runtimeState.seamlessScrollMode > 0) {
      attachSeamlessScrollListener();
    }
    ensureDefaultCallbacks();
    safeInvokeCallback("onSkinEnter");
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
        postRequest("addWhere", buildRangePayload(startIndex, count, { where: where })),
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
      return postRequest("getInfo", { movieId: movieId });
    },

    getInfos: function (movieIdsOrStartIndex, countOrPayload) {
      var payload = {};

      if (Array.isArray(movieIdsOrStartIndex)) {
        payload = { movieIds: movieIdsOrStartIndex };
      } else if (movieIdsOrStartIndex && typeof movieIdsOrStartIndex === "object") {
        payload = normalizeRangePayloadObject(movieIdsOrStartIndex);
      } else if (movieIdsOrStartIndex !== undefined || countOrPayload !== undefined) {
        payload = buildRangePayload(movieIdsOrStartIndex, countOrPayload);
      }

      return postRequest("getInfos", payload);
    },

    getFindInfo: function () {
      return postRequest("getFindInfo", {});
    },

    getFocusThum: function () {
      return postRequest("getFocusThum", {});
    },

    getSelectThums: function () {
      return postRequest("getSelectThums", {});
    },

    getProfile: function (key) {
      return postRequest("getProfile", { key: key });
    },

    writeProfile: function (key, value) {
      return postRequest("writeProfile", { key: key, value: value });
    },

    changeSkin: function (skinName) {
      return postRequest("changeSkin", { skinName: skinName });
    },

    // focus / select は compat 側で状態遷移を畳み、callback 二重発火を防ぐ。
    focusThum: function (movieId) {
      return postRequest("focusThum", { movieId: movieId }).then(function (payload) {
        ensureDefaultCallbacks();
        var resolvedMovieId = payload && (payload.movieId || payload.id || movieId || 0);
        var selected = !!(payload && payload.selected);

        if (resolvedMovieId) {
          synchronizeFocusState(payload, resolvedMovieId);
          applySelectionState(resolvedMovieId, selected);
        }

        return payload;
      });
    },

    selectThum: function (movieId, selected) {
      return postRequest("selectThum", {
        movieId: movieId,
        selected: selected === undefined ? true : !!selected
      }).then(function (payload) {
        ensureDefaultCallbacks();
        var resolvedMovieId = payload && (payload.movieId || payload.id || movieId || 0);
        var isSelected = !!(payload && (payload.selected !== undefined ? payload.selected : payload.focused));

        if (resolvedMovieId) {
          synchronizeFocusState(payload, resolvedMovieId);
          applySelectionState(resolvedMovieId, isSelected);
        }

        return payload;
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

    getSkinName: function () {
      return postRequest("getSkinName", {});
    },

    getDBName: function () {
      return postRequest("getDBName", {});
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
