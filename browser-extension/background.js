"use strict";

importScripts("providers.js");

const HOST = "com.multiagenthub.browser_chat";
const MAX_PROMPT = 200000;
const MAX_RESULT = 64000;
const bindings = new Map();
const requests = new Map();
const seenRequestIds = new Set();
let nativePort = null;
let nativeReady = false;
let nativeHandshake = null;
let nativeError = "Chưa kết nối cầu nối trên máy.";
let restorePromise;
let reconnecting = null;
let reconnectAfter = 0;
const tabUpdates = new Map();

const providers = globalThis.MultiAgentHubProviders;
const providerOf = value => providers.providerOf(value);
const freshChatUrl = value => providers.fresh(value);
async function inject(tabId) {
  await chrome.scripting.executeScript({target: {tabId, frameIds: [0]}, files: ["providers.js", "discovery.js", "content.js"]});
  try { await chrome.scripting.executeScript({target: {tabId, frameIds: [0]}, world: "MAIN", files: ["network-protocol.js", "network.js"]}); }
  catch { /* MAIN observer blocked: keep DOM fallback without escalating permissions. */ }
}
function publicBinding(binding) {
  return {id: binding.id, provider: binding.provider, tabId: binding.tabId,
    url: binding.url, title: binding.title, model: binding.model, ready: true, siteName: (providers.site(binding.url)?.name || "").slice(0, 200),
    detectedModel: binding.detectedModel || "", modelSource: binding.modelSource || "unknown", transport: binding.transport || "dom"};
}

async function persist() {
  await chrome.storage.session.set({bindings: [...bindings.values()]});
}

function postNative(message) {
  if (!nativePort || !nativeReady) return false;
  try { nativePort.postMessage(message); return true; }
  catch { closeNative(nativePort, "Không gửi được đến Hub. Kết nối lại cầu nối trên máy.", true); return false; }
}

function publishState() {
  return postNative({type: "state", bindings: [...bindings.values()].map(publicBinding)});
}

function restore() {
  if (!restorePromise) restorePromise = (async () => {
    const saved = (await chrome.storage.session.get("bindings")).bindings;
    if (!Array.isArray(saved)) return;
    for (const item of saved) {
      try {
        const tab = await chrome.tabs.get(item.tabId);
        if (tab.url !== item.url || providerOf(tab.url) !== item.provider) continue;
        const check = await chrome.tabs.sendMessage(tab.id, {type: "hub-ping", nonce: item.nonce}, {frameId: 0});
        if (check?.nonce === item.nonce && check.url === item.url) bindings.set(item.id, item);
      } catch { /* A browser reload requires a new explicit connection. */ }
    }
    await persist();
  })();
  return restorePromise;
}

function closeNative(port, reason, disconnect = false) {
  if (nativePort !== port) return;
  const wasReady = nativeReady;
  nativeError = reason;
  nativePort = null;
  nativeReady = false;
  reconnectAfter = wasReady ? 0 : Date.now() + 15000;
  const handshake = nativeHandshake;
  nativeHandshake = null;
  if (handshake) {
    clearTimeout(handshake.timer);
    handshake.reject(new Error(reason));
  }
  for (const request of requests.values()) {
    void chrome.tabs.sendMessage(request.tabId, {type: "hub-cancel", id: request.id, nonce: request.nonce}, {frameId: 0}).catch(() => {});
  }
  requests.clear();
  if (disconnect) { try { port.disconnect(); } catch { /* The transport may already be closed. */ } }
}

async function reconnectNative() {
  if (reconnecting) return reconnecting;
  reconnecting = (async () => {
    await restore();
    // Reconnect only the exact documents previously linked by the user, including existing conversations.
    for (const binding of [...bindings.values()]) {
      try {
        const tab = await chrome.tabs.get(binding.tabId);
        const check = await chrome.tabs.sendMessage(binding.tabId, {type: "hub-ping", nonce: binding.nonce}, {frameId: 0});
        if (tab.url !== binding.url || providerOf(tab.url) !== binding.provider || check?.nonce !== binding.nonce || check.url !== binding.url) throw new Error("stale");
      } catch {
        if (bindings.get(binding.id) === binding) await removeBinding(binding.id, "Tab đã tải lại hoặc đổi trang; cần liên kết lại đúng tab.");
      }
    }
    if (!bindings.size) throw new Error("Chưa có tab hợp lệ để đồng bộ. Mở chat mới và Kết nối tab hiện tại.");
    reconnectAfter = Date.now() + 15000;
    await openNative();
    if (!publishState()) throw new Error(nativeError || "Chưa gửi được danh sách tab đến Hub.");
  })();
  try { return await reconnecting; }
  finally { reconnecting = null; }
}

function openNative() {
  if (nativeReady && nativePort) return Promise.resolve();
  if (nativeHandshake) return nativeHandshake.promise;
  const port = chrome.runtime.connectNative(HOST);
  nativePort = port;
  nativeReady = false;
  nativeError = "";
  let resolveHandshake, rejectHandshake;
  const promise = new Promise((resolve, reject) => { resolveHandshake = resolve; rejectHandshake = reject; });
  nativeHandshake = {promise, resolve: resolveHandshake, reject: rejectHandshake,
    timer: setTimeout(() => closeNative(port,
      "Hub chưa xác nhận cầu nối sau 12 giây. Mở bản Hub hỗ trợ Browser Chat và kết nối lại; chưa gửi yêu cầu chatbot.", true), 12000)};
  port.onMessage.addListener(message => {
    if (nativePort !== port) return;
    if (!nativeReady) {
      if (message?.type !== "hello") return; // Nothing may execute before the Hub handshake.
      if (message.protocol !== 1) {
        closeNative(port, "Phiên bản cầu nối không tương thích. Cập nhật Hub và tiện ích rồi kết nối lại.", true); return;
      }
      nativeReady = true;
      nativeError = "";
      const handshake = nativeHandshake;
      if (!publishState()) return; // A failed send rejects the pending handshake via closeNative.
      nativeHandshake = null;
      clearTimeout(handshake.timer);
      handshake.resolve();
      return;
    }
    void onNative(message, port);
  });
  port.onDisconnect.addListener(() => {
    const error = chrome.runtime.lastError?.message || "Cầu nối đã ngắt. Mở Hub và kết nối lại.";
    closeNative(port, error);
  });
  return promise;
}

function finishError(id, message) {
  if (!requests.has(id)) return;
  requests.delete(id);
  postNative({type: "error", id, message});
}

async function removeBinding(id, reason) {
  const binding = bindings.get(id);
  if (!binding) return;
  bindings.delete(id);
  for (const request of [...requests.values()].filter(item => item.bindingId === id)) {
    void chrome.tabs.sendMessage(request.tabId, {type: "hub-cancel", id: request.id, nonce: request.nonce}, {frameId: 0}).catch(() => {});
    finishError(request.id, reason);
  }
  await persist();
  publishState();
  postNative({type: "attention", bindingId: id, message: reason});
}

async function onNative(message, port) {
  await restore();
  if (nativePort !== port || !nativeReady) return;
  if (!message || typeof message !== "object") return;
  if (message.type === "state-request") { publishState(); return; }
  if (message.type === "cancel") {
    const request = requests.get(message.id);
    if (!request) return;
    void chrome.tabs.sendMessage(request.tabId, {type: "hub-cancel", id: request.id, nonce: request.nonce}, {frameId: 0}).catch(() => {});
    finishError(request.id, "Đã hủy theo yêu cầu Hub. Nếu website còn tạo câu trả lời, bạn có thể dừng trực tiếp tại tab.");
    return;
  }
  if (message.type !== "request" || typeof message.id !== "string" || message.id.length > 128) return;
  if (seenRequestIds.has(message.id)) return; // Never replay a submitted request, including a completed one.
  seenRequestIds.add(message.id);
  if (seenRequestIds.size > 2048) seenRequestIds.delete(seenRequestIds.values().next().value);
  const binding = bindings.get(message.bindingId);
  const fail = text => postNative({type: "error", id: message.id, message: text});
  if (!binding) { fail("Tab chưa được kết nối hoặc đã đổi trang. Kết nối lại trong tiện ích."); return; }
  if (typeof message.prompt !== "string" || !message.prompt.trim() || message.prompt.length > MAX_PROMPT) {
    fail("Yêu cầu rỗng hoặc dài quá 200.000 ký tự."); return;
  }
  if (message.expectedModel !== binding.model) {
    fail("Model trong Hub khác nhãn model bạn đã xác nhận ở tiện ích. Chọn và xác nhận lại; tiện ích không tự đổi model."); return;
  }
  if ([...requests.values()].some(item => item.bindingId === binding.id)) {
    fail("Tab đang xử lý một yêu cầu khác. Không gửi chồng hoặc thay nội dung người dùng."); return;
  }
  try {
    const tab = await chrome.tabs.get(binding.tabId);
    if (nativePort !== port || !nativeReady) return;
    if (tab.url !== binding.url || providerOf(tab.url) !== binding.provider) {
      await removeBinding(binding.id, "Tab đã đổi cuộc trò chuyện. Kết nối lại trước khi tiếp tục.");
      fail("Tab đã đổi cuộc trò chuyện. Chưa gửi yêu cầu."); return;
    }
    requests.set(message.id, {id: message.id, bindingId: binding.id, tabId: binding.tabId,
      nonce: binding.nonce, url: binding.url});
    const response = await chrome.tabs.sendMessage(binding.tabId, {type: "hub-request", id: message.id,
      nonce: binding.nonce, url: binding.url, prompt: message.prompt, selectors: binding.selectors}, {frameId: 0});
    if (!response?.accepted) finishError(message.id, response?.message || "Website chưa nhận yêu cầu. Không tự gửi lại.");
  } catch {
    finishError(message.id, "Không liên lạc được với tab. Yêu cầu có thể đã gửi; kiểm tra tab trước khi thử lại.");
    await removeBinding(binding.id, "Mất kết nối tab. Kết nối lại trong tiện ích.");
  }
}

function cleanSelectors(value) {
  const result = {};
  for (const key of ["input", "send", "user", "assistant", "response", "stop", "actions", "busy"]) {
    if (typeof value?.[key] === "string" && value[key].trim()) {
      if (value[key].length > 1000) throw new Error("Bộ chọn CSS dài quá 1.000 ký tự.");
      result[key] = value[key].trim();
    }
  }
  return result;
}

async function onPopup(message) {
  await restore();
  if (message.type === "popup-status") {
    if (bindings.size && !nativePort && !nativeHandshake && !reconnecting && Date.now() >= reconnectAfter) {
      void reconnectNative().catch(error => { nativeError = error.message; });
    }
    const [tab] = await chrome.tabs.query({active: true, currentWindow: true});
    return {connected: !!nativePort && nativeReady, connecting: !!nativeHandshake, error: nativeError, canBindNew: freshChatUrl(tab?.url), tab: tab ? {id: tab.id, url: tab.url,
      provider: providerOf(tab.url), siteName: providers.site(tab.url)?.name, experimental: providers.site(tab.url)?.experimental === true} : null, bindings: [...bindings.values()].map(publicBinding)};
  }
  if (message.type === "popup-resync") {
    if (requests.size) return {ok: false, error: "Có lượt chatbot đang chạy. Đợi hoàn tất trước khi đồng bộ lại để giữ nguyên lượt này."};
    await reconnectNative();
    return {ok: true, count: bindings.size};
  }
  if (message.type === "popup-disconnect") {
    await removeBinding(message.id, "Bạn đã ngắt kết nối tab. Website có thể vẫn đang tạo phản hồi.");
    return {ok: true};
  }
  if (message.type === "popup-scan" || message.type === "popup-pick") {
    const [tab] = await chrome.tabs.query({active: true, currentWindow: true});
    const provider = providerOf(tab?.url);
    if (!provider || tab.id !== message.tabId) return {ok: false, error: "Mở tab chatbot HTTPS cần quét rồi thử lại."};
    if (message.type === "popup-pick" && [...requests.values()].some(item => item.tabId === tab.id)) {
      return {ok: false, error: "Tab đang chạy. Đợi lượt đang chạy hoàn tất trước khi chọn ô nhập."};
    }
    const selectors = cleanSelectors(message.selectors);
    await inject(tab.id);
    const result = await chrome.tabs.sendMessage(tab.id,
      {type: message.type === "popup-pick" ? "hub-pick" : "hub-scan", provider, selectors}, {frameId: 0});
    const current = await chrome.tabs.get(tab.id);
    if (current.url !== tab.url || result?.url !== tab.url) return {ok: false,
      error: "Tab đã đổi trang hoặc còn script extension cũ. Tải lại trang chatbot rồi mở extension để quét lại."};
    return result;
  }
  if (message.type !== "popup-connect") return {ok: false, error: "Lệnh không được hỗ trợ."};
  if (message.confirmed !== true || typeof message.model !== "string"
      || message.model.length > 120 || /[\u0000-\u001f\u007f-\u009f]/.test(message.model)) {
    return {ok: false, error: "Xác nhận đã mở chat mới trống và chọn model trên website."};
  }
  const [tab] = await chrome.tabs.query({active: true, currentWindow: true});
  const provider = providerOf(tab?.url);
  if (!provider || tab.id !== message.tabId) return {ok: false, error: "Mở tab chatbot HTTPS và bấm extension ngay tại tab đó."};
  if (!freshChatUrl(tab.url)) return {ok: false, error: "Mở chat mới trên website rồi kết nối; để tiếp tục chủ đề cũ dùng lịch sử Hub, bridge sẽ chuyển context sang tab mới. Lịch sử website được giữ nguyên."};
  if ([...requests.values()].some(item => item.tabId === tab.id)) return {ok: false, error: "Tab đang chạy. Dừng lượt ở Hub trước khi đổi kết nối/model."};
  if (bindings.size >= 128 && ![...bindings.values()].some(item => item.tabId === tab.id)) {
    return {ok: false, error: "Đã kết nối 128 tab. Ngắt một tab trước khi thêm tab khác."};
  }
  const selectors = cleanSelectors(message.selectors);
  await inject(tab.id);
  const check = await chrome.tabs.sendMessage(tab.id, {type: "hub-bind", provider, selectors}, {frameId: 0});
  if (!check?.ready || check.url !== tab.url) return {ok: false, error: check?.message || "Không nhận diện được khung nhập. Đăng nhập, mở chat và kiểm tra bộ chọn CSS nâng cao."};
  await openNative();
  const current = await chrome.tabs.get(tab.id);
  // Recheck freshness after waiting for Hub: the website could load an old turn during the handshake.
  const currentDocument = await chrome.tabs.sendMessage(tab.id, {type: "hub-bind", provider, selectors}, {frameId: 0});
  if (!currentDocument?.ready) return {ok: false, error: currentDocument?.message || "Chat không còn trống. Mở chat mới trên website rồi kết nối."};
  if (current.url !== tab.url || currentDocument?.nonce !== check.nonce || currentDocument.url !== tab.url) {
    return {ok: false, error: "Tab đã thay đổi trong lúc chờ Hub. Kết nối lại để xác nhận đúng cuộc trò chuyện."};
  }
  if (!nativeReady || !nativePort) return {ok: false, error: nativeError || "Cầu nối đã ngắt trước khi gắn tab."};
  if ([...requests.values()].some(item => item.tabId === tab.id)) return {ok: false, error: "Tab vừa nhận yêu cầu khác. Đợi lượt đó hoàn tất trước khi đổi kết nối/model."};
  const model = message.model.trim() || "Theo website";
  const previous = [...bindings.values()].find(item => item.tabId === tab.id);
  const sameDocument = previous?.provider === provider && previous.url === tab.url && previous.nonce === check.nonce
    && previous.model.trim().toLowerCase() === model.toLowerCase();
  if (previous) bindings.delete(previous.id);
  const binding = {id: sameDocument ? previous.id : crypto.randomUUID(), provider, tabId: tab.id, url: tab.url,
    title: (tab.title || provider).slice(0, 200), model, nonce: check.nonce, selectors, detectedModel: currentDocument.model || "",
    modelSource: currentDocument.model ? "page" : "unknown", transport: "dom"};
  bindings.set(binding.id, binding);
  await persist();
  await chrome.storage.local.set({["settings:" + provider + ":" + new URL(tab.url).origin]: {model: binding.model, selectors}});
  if (!publishState()) return {ok: false, error: nativeError || "Không gửi được trạng thái tab đến Hub. Chưa kết nối thành công."};
  return {ok: true};
}

chrome.runtime.onMessage.addListener((message, sender, reply) => {
  if (sender.id !== chrome.runtime.id || !message) return false;
  if (!sender.tab && typeof message.type === "string" && message.type.startsWith("popup-")) {
    onPopup(message).then(reply, error => reply({ok: false, error: error.message || "Không thể kết nối."}));
    return true;
  }
  if (!sender.tab || sender.frameId !== 0 || !providerOf(sender.url)) return false;
  const request = requests.get(message.id);
  if (!request || request.tabId !== sender.tab.id || request.nonce !== message.nonce) return false;
  const binding = bindings.get(request.bindingId);
  if (!binding || sender.url !== binding.url || message.url !== binding.url) return false;
  if (message.type === "hub-observation") {
    if (typeof message.detectedModel !== "string" || message.detectedModel.length > 120 || /[\u0000-\u001f\u007f-\u009f]/.test(message.detectedModel)
        || !["response", "page", "unknown"].includes(message.modelSource)
        || !["dom", "chat-completions", "responses", "messages", "chatgpt-message", "generate-content", "gemini-rpc"].includes(message.transport)) return false;
    binding.detectedModel = message.detectedModel.trim(); binding.modelSource = message.modelSource; binding.transport = message.transport;
    void persist(); publishState();
  } else if (message.type === "hub-delta" && typeof message.text === "string" && message.text.length <= MAX_RESULT) {
    postNative({type: "delta", id: request.id, text: message.text});
  } else if (message.type === "hub-result" && typeof message.text === "string" && message.text.trim() && message.text.length <= MAX_RESULT) {
    requests.delete(request.id);
    postNative({type: "result", id: request.id, text: message.text});
  } else if (message.type === "hub-error" && typeof message.message === "string") {
    finishError(request.id, message.message.slice(0, 2000));
  }
  return false;
});

chrome.tabs.onRemoved.addListener(tabId => {
  for (const binding of [...bindings.values()].filter(item => item.tabId === tabId)) void removeBinding(binding.id, "Tab chatbot đã đóng.");
});

async function reconcileTab(tabId, change) {
  await restore();
  for (const binding of [...bindings.values()].filter(item => item.tabId === tabId)) {
    if (change.discarded === true) {
      await removeBinding(binding.id, "Trình duyệt đã đưa tab vào trạng thái ngủ. Nội dung nhận được vẫn được giữ; mở lại tab và liên kết lại.");
      continue;
    }
    let documentState = null;
    let tab = null;
    // Chrome also reports loading during SPA updates. Only document identity can prove a reload.
    for (let attempt = 0; attempt < 3; attempt++) {
      documentState = null;
      let timer;
      try {
        tab = await chrome.tabs.get(tabId);
        documentState = await Promise.race([
          chrome.tabs.sendMessage(tabId, {type: "hub-ping", nonce: binding.nonce}, {frameId: 0}),
          new Promise(resolve => { timer = setTimeout(() => resolve(null), 1500); })
        ]);
        if (documentState?.nonce && documentState.nonce !== binding.nonce) break;
        if (documentState?.nonce && tab.url !== documentState.url) tab = await chrome.tabs.get(tabId);
        if (documentState?.nonce && tab.url === documentState.url) break;
      } catch { /* Retry a briefly unavailable content channel; never inject/replay into a new document. */ }
      finally { clearTimeout(timer); }
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    if (bindings.get(binding.id) !== binding) continue;
    if (documentState?.nonce !== binding.nonce) {
      await removeBinding(binding.id, "Document của tab đã thay đổi hoặc mất kết nối. Đã giữ phần trả lời; liên kết lại đúng cuộc trò chuyện, Hub không tự gửi lại prompt.");
      continue;
    }
    const url = documentState.url;
    if (tab?.url !== url || providerOf(url) !== binding.provider) {
      await removeBinding(binding.id, "Tab đã đổi trang chatbot. Kết nối lại trước khi tiếp tục.");
      continue;
    }
    if (url !== binding.url) {
        const active = [...requests.values()].find(item => item.bindingId === binding.id);
        try {
          let check = null;
          if (active) {
            // The SPA can assign its first conversation URL just before it inserts our user turn.
            for (let attempt = 0; attempt < 8; attempt++) {
              if (!bindings.has(binding.id) || requests.get(active.id) !== active) break;
              check = await chrome.tabs.sendMessage(tabId, {type: "hub-navigation", id: active.id,
                nonce: binding.nonce, from: binding.url, to: url}, {frameId: 0});
              if (check?.allowed) break;
              await new Promise(resolve => setTimeout(resolve, 200));
            }
          }
          if (check?.allowed && check.nonce === binding.nonce && check.url === url) {
            binding.url = url;
            active.url = url;
            await persist(); publishState();
          } else await removeBinding(binding.id, "Tab đã chuyển cuộc trò chuyện. Kết nối lại trước khi gửi tiếp.");
        } catch { await removeBinding(binding.id, "Tab đã đổi trang hoặc mất kết nối."); }
    }
  }
}

chrome.tabs.onUpdated.addListener((tabId, change) => {
  if (!change.url && !change.status && change.discarded !== true) return;
  // Serialize loading/url/complete notifications for this tab so a stale event cannot undo a newer URL.
  const next = (tabUpdates.get(tabId) || Promise.resolve()).catch(() => {}).then(() => reconcileTab(tabId, change));
  tabUpdates.set(tabId, next);
  void next.finally(() => { if (tabUpdates.get(tabId) === next) tabUpdates.delete(tabId); }).catch(() => {});
});
