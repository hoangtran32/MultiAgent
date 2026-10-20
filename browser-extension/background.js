"use strict";

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

function providerOf(value) {
  try {
    const url = new URL(value);
    if (url.username || url.password) return null;
    if (url.origin === "https://gemini.google.com" && /^\/app(?:\/[^/]+)?\/?$/.test(url.pathname)) return "gemini-web";
    if (url.origin === "https://chatgpt.com" && /^\/(?:c\/[^/]+)?\/?$/.test(url.pathname)) return "chatgpt-web";
  } catch { /* An unsupported page cannot become a binding. */ }
  return null;
}

function freshChatUrl(value) {
  try {
    const provider = providerOf(value);
    const path = new URL(value).pathname;
    return provider === "gemini-web" ? /^\/app\/?$/.test(path) : provider === "chatgpt-web" && path === "/";
  } catch { return false; }
}

function publicBinding(binding) {
  return {id: binding.id, provider: binding.provider, tabId: binding.tabId,
    url: binding.url, title: binding.title, model: binding.model, ready: true};
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
  nativeError = reason;
  nativePort = null;
  nativeReady = false;
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
    const [tab] = await chrome.tabs.query({active: true, currentWindow: true});
    return {connected: !!nativePort && nativeReady, connecting: !!nativeHandshake, error: nativeError, canBindNew: freshChatUrl(tab?.url), tab: tab ? {id: tab.id, url: tab.url,
      provider: providerOf(tab.url)} : null, bindings: [...bindings.values()].map(publicBinding)};
  }
  if (message.type === "popup-disconnect") {
    await removeBinding(message.id, "Bạn đã ngắt kết nối tab. Website có thể vẫn đang tạo phản hồi.");
    return {ok: true};
  }
  if (message.type !== "popup-connect") return {ok: false, error: "Lệnh không được hỗ trợ."};
  if (message.confirmed !== true || typeof message.model !== "string" || !message.model.trim()
      || message.model.length > 120 || /[\u0000-\u001f\u007f-\u009f]/.test(message.model)) {
    return {ok: false, error: "Nhập tên model và xác nhận bạn đã chọn model đó trên website."};
  }
  const [tab] = await chrome.tabs.query({active: true, currentWindow: true});
  const provider = providerOf(tab?.url);
  if (!provider || tab.id !== message.tabId) return {ok: false, error: "Mở tab gemini.google.com/app hoặc chatgpt.com rồi kết nối lại."};
  if (!freshChatUrl(tab.url)) return {ok: false, error: "Mở chat mới trên website rồi kết nối; để tiếp tục chủ đề cũ dùng lịch sử Hub, bridge sẽ chuyển context sang tab mới. Lịch sử website được giữ nguyên."};
  if ([...requests.values()].some(item => item.tabId === tab.id)) return {ok: false, error: "Tab đang chạy. Dừng lượt ở Hub trước khi đổi kết nối/model."};
  if (bindings.size >= 128 && ![...bindings.values()].some(item => item.tabId === tab.id)) {
    return {ok: false, error: "Đã kết nối 128 tab. Ngắt một tab trước khi thêm tab khác."};
  }
  const selectors = cleanSelectors(message.selectors);
  await chrome.scripting.executeScript({target: {tabId: tab.id, frameIds: [0]}, files: ["content.js"]});
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
  const previous = [...bindings.values()].find(item => item.tabId === tab.id);
  if (previous) bindings.delete(previous.id);
  const binding = {id: crypto.randomUUID(), provider, tabId: tab.id, url: tab.url,
    title: (tab.title || provider).slice(0, 200), model: message.model.trim(), nonce: check.nonce, selectors};
  bindings.set(binding.id, binding);
  await persist();
  await chrome.storage.local.set({["settings:" + provider]: {model: binding.model, selectors}});
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
  if (message.type === "hub-delta" && typeof message.text === "string" && message.text.length <= MAX_RESULT) {
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

chrome.tabs.onUpdated.addListener((tabId, change) => {
  void (async () => {
    await restore();
    for (const binding of [...bindings.values()].filter(item => item.tabId === tabId)) {
      if (change.status === "loading") {
        await removeBinding(binding.id, "Tab đã tải lại. Kết nối lại để xác nhận đúng cuộc trò chuyện và model.");
      } else if (change.url && change.url !== binding.url) {
        const active = [...requests.values()].find(item => item.bindingId === binding.id);
        try {
          let check = null;
          if (active && providerOf(change.url) === binding.provider) {
            // The SPA can assign its first conversation URL just before it inserts our user turn.
            for (let attempt = 0; attempt < 8; attempt++) {
              if (!bindings.has(binding.id) || requests.get(active.id) !== active) break;
              check = await chrome.tabs.sendMessage(tabId, {type: "hub-navigation", id: active.id,
                nonce: binding.nonce, from: binding.url, to: change.url}, {frameId: 0});
              if (check?.allowed) break;
              await new Promise(resolve => setTimeout(resolve, 200));
            }
          }
          if (check?.allowed && check.nonce === binding.nonce && check.url === change.url) {
            binding.url = change.url;
            active.url = change.url;
            await persist(); publishState();
          } else await removeBinding(binding.id, "Tab đã chuyển cuộc trò chuyện. Kết nối lại trước khi gửi tiếp.");
        } catch { await removeBinding(binding.id, "Tab đã đổi trang hoặc mất kết nối."); }
      }
    }
  })();
});
