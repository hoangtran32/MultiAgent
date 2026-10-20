"use strict";

const statusBox = document.getElementById("status");
const form = document.getElementById("connect-form");
const model = document.getElementById("model");
const confirmed = document.getElementById("confirmed");
const connect = document.getElementById("connect");
const scanButton = document.getElementById("scan");
const pickButton = document.getElementById("pick");
const scanStatus = document.getElementById("scan-status");
const fields = {
  input: "Khung nhập", send: "Nút gửi", user: "Tin nhắn người dùng", assistant: "Tin nhắn AI",
  response: "Phần nội dung trong tin AI", stop: "Nút dừng tạo", actions: "Nút sao chép khi AI đã xong", busy: "Dấu hiệu đang tạo trong tin AI"
};
let currentTab = null;
let canBindNewTab = false;
let busy = false;
let scanning = false;
let modelEdited = false;
model.addEventListener("input", () => { modelEdited = true; confirmed.checked = false; });

function customSelectors() {
  return Object.fromEntries(Object.keys(fields).map(key => [key, document.getElementById("selector-" + key).value.trim()]));
}

async function scanTab(reset = false) {
  if (scanning || !currentTab?.provider) return;
  scanning = true; scanButton.disabled = true; pickButton.disabled = true;
  scanStatus.classList.remove("error"); scanStatus.textContent = "Đang quét vùng soạn chat…";
  if (reset) for (const key of ["input", "send"]) document.getElementById("selector-" + key).value = "";
  try {
    const response = await chrome.runtime.sendMessage({type: "popup-scan", tabId: currentTab.id, selectors: customSelectors()});
    if (response?.model && (!modelEdited || reset)) {
      if (model.value !== response.model) confirmed.checked = false;
      model.value = response.model;
    }
    if (!response?.ok) throw new Error(response?.error || "Chưa nhận diện được khung chat.");
    scanStatus.textContent = (response.picked ? "Đã nhận ô nhập bạn chọn." : "Đã tự nhận diện khung chat.")
      + (response.sendFound ? " Đã thấy nút gửi." : " Nút gửi chưa hiện; sẽ quét lại khi có nội dung.")
      + (response.model ? "\nNhãn model đọc từ giao diện: " + response.model : "\nChưa đọc được nhãn model; nhập nhãn đang hiển thị bên dưới.")
      + (response.draft ? "\nÔ nhập có bản nháp. Tiện ích giữ nguyên; cần chat mới trống để kết nối." : "");
  } catch (error) { scanStatus.textContent = error.message; scanStatus.classList.add("error"); }
  finally { scanning = false; scanButton.disabled = !currentTab?.provider; pickButton.disabled = !currentTab?.provider; }
}
scanButton.addEventListener("click", () => { void scanTab(true); });
pickButton.addEventListener("click", async () => {
  if (!currentTab?.provider || busy || scanning) return;
  try {
    const response = await chrome.runtime.sendMessage({type: "popup-pick", tabId: currentTab.id});
    if (!response?.ok) throw new Error(response?.error || "Không mở được bộ chọn.");
    // A stale explicit selector must not hide the field selected on the page.
    const key = "settings:" + currentTab.provider;
    const saved = (await chrome.storage.local.get(key))[key] || {};
    delete saved.selectors?.input;
    await chrome.storage.local.set({[key]: saved});
    window.close();
  } catch (error) { scanStatus.textContent = error.message; scanStatus.classList.add("error"); }
});

for (const [key, text] of Object.entries(fields)) {
  const label = document.createElement("label");
  label.htmlFor = "selector-" + key;
  label.textContent = text;
  const input = document.createElement("input");
  input.id = label.htmlFor;
  input.maxLength = 1000;
  input.spellcheck = false;
  input.placeholder = "Mặc định";
  document.getElementById("selectors").append(label, input);
}

function show(text, error = false) {
  statusBox.textContent = text;
  statusBox.classList.toggle("error", error);
}

async function refresh(loadSettings = false) {
  const state = await chrome.runtime.sendMessage({type: "popup-status"});
  currentTab = state.tab;
  canBindNewTab = state.canBindNew === true;
  const providerName = currentTab?.provider === "gemini-web" ? "Gemini" : currentTab?.provider === "chatgpt-web" ? "ChatGPT" : null;
  connect.disabled = busy || !providerName || !canBindNewTab;
  scanButton.disabled = busy || scanning || !providerName;
  pickButton.disabled = busy || scanning || !providerName;
  if (!providerName) show("Mở tab gemini.google.com/app hoặc chatgpt.com để kết nối.");
  else if (!canBindNewTab) show(providerName + " · " + (state.connected ? "Cầu nối đã kết nối.\n" : "")
    + "Để tạo liên kết mới, mở chat mới trống trên website. Tiếp tục chủ đề cũ từ lịch sử Hub để gửi ngữ cảnh sang tab mới. Các liên kết đang chạy được giữ nguyên."
    + (state.error ? "\n" + state.error : ""), !!state.error);
  else if (state.connected) show(providerName + " · Cầu nối trên máy đã kết nối.\nChọn model ở website rồi xác nhận bên dưới.");
  else if (state.connecting) show(providerName + " · Đang chờ Hub xác nhận cầu nối (tối đa 12 giây)…");
  else show(providerName + " · " + (state.error || "Chưa kết nối Hub. Bấm Kết nối tab hiện tại."), !!state.error);
  const container = document.getElementById("bindings");
  container.replaceChildren();
  if (!state.bindings.length) {
    const empty = document.createElement("p"); empty.className = "empty"; empty.textContent = "Chưa có tab nào."; container.append(empty);
  }
  for (const binding of state.bindings) {
    const row = document.createElement("div"); row.className = "binding";
    const description = document.createElement("div"); description.className = "description";
    const name = document.createElement("strong"); name.textContent = binding.provider === "gemini-web" ? "Gemini" : "ChatGPT";
    const label = document.createElement("small"); label.textContent = binding.model + " · do bạn xác nhận";
    const title = document.createElement("small"); title.textContent = binding.title; title.title = binding.url;
    description.append(name, label, title);
    const button = document.createElement("button"); button.type = "button"; button.textContent = "Ngắt";
    button.addEventListener("click", async () => {
      button.disabled = true;
      try { await chrome.runtime.sendMessage({type: "popup-disconnect", id: binding.id}); await refresh(); }
      catch (error) { show(error.message, true); button.disabled = false; }
    });
    row.append(description, button); container.append(row);
  }
  if (loadSettings && currentTab?.provider) {
    const key = "settings:" + currentTab.provider;
    const saved = (await chrome.storage.local.get(key))[key];
    if (saved) {
      model.value = saved.model || "";
      for (const field of Object.keys(fields)) document.getElementById("selector-" + field).value = saved.selectors?.[field] || "";
    }
  }
  return state;
}

form.addEventListener("submit", async event => {
  event.preventDefault();
  if (busy || !currentTab?.provider || !canBindNewTab || !form.reportValidity()) return;
  busy = true; connect.disabled = true;
  show("Đang kết nối tab với Hub…");
  try {
    const selectors = customSelectors();
    const response = await chrome.runtime.sendMessage({type: "popup-connect", tabId: currentTab.id,
      model: model.value.trim(), confirmed: confirmed.checked, selectors});
    if (!response?.ok) throw new Error(response?.error || "Không thể kết nối.");
    confirmed.checked = false;
    const state = await refresh();
    if (!state.connected) throw new Error(state.error || "Cầu nối đã ngắt trước khi xác nhận kết nối. Mở Hub và kết nối lại.");
    show("Đã gắn tab. Trong Hub, làm mới danh sách chatbot trình duyệt rồi chọn tab này. Tên model do bạn xác nhận; Hub không tự đổi model.");
  } catch (error) { show(error.message, true); }
  finally { busy = false; connect.disabled = !currentTab?.provider || !canBindNewTab; }
});

void refresh(true).then(() => scanTab()).catch(error => show(error.message || "Không đọc được trạng thái tiện ích.", true));
// Native host launch errors arrive asynchronously after connectNative returns.
setInterval(() => { if (!busy) void refresh().catch(error => show(error.message, true)); }, 2000);
