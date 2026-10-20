(() => {
  "use strict";
  // Older injected documents need a reload, rather than two competing listeners.
  if (globalThis.__multiAgentHubBrowserContent) return;
  globalThis.__multiAgentHubBrowserContent = "1.1.0";
  const nonce = crypto.randomUUID();
  let active = null;
  let pickedInput = null;
  let cancelPicker = null;
  const discovery = globalThis.MultiAgentHubDiscovery;
  const defaults = {
    "chatgpt-web": {
      input: '#prompt-textarea, textarea[data-testid="prompt-textarea"]',
      send: 'button[data-testid="send-button"], button[aria-label="Send prompt"], button[aria-label="Send message"], button[aria-label="Gửi tin nhắn"]',
      user: '[data-message-author-role="user"]', assistant: '[data-message-author-role="assistant"]',
      response: '.markdown',
      stop: 'button[data-testid="stop-button"], button[aria-label="Stop streaming"], button[aria-label="Stop generating"], button[aria-label="Dừng tạo"]',
      actions: 'button[data-testid="copy-turn-action-button"], button[aria-label="Copy response"], button[aria-label="Sao chép"]',
      busy: '[data-is-streaming="true"], [aria-busy="true"]'
    },
    "gemini-web": {
      input: 'rich-textarea .ql-editor[contenteditable="true"], rich-textarea [contenteditable="true"], .text-input-field textarea',
      send: 'button.send-button, button[aria-label="Send message"], button[aria-label="Gửi tin nhắn"], button[aria-label="Send"]',
      user: 'user-query, [data-test-id="user-query"]', assistant: 'model-response, [data-test-id="model-response"]',
      response: 'message-content, .markdown, .model-response-text',
      stop: 'button.send-button.stop, button.send-button:has(mat-icon[data-mat-icon-name="stop"]), button.send-button:has(mat-icon[fonticon="stop"]), button[aria-label="Stop response"], button[aria-label="Stop generating"], button[aria-label="Stop"], button[aria-label="Dừng câu trả lời"], button[aria-label="Dừng phản hồi"]',
      actions: 'button[data-test-id="copy-button"], button[data-test-id="copy-response-button"], button.copy-button, button[aria-label="Copy response"], button[aria-label="Sao chép phản hồi"]',
      busy: '[aria-busy="true"], [data-is-streaming="true"]'
    }
  };
  const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
  const normal = text => String(text || "").replace(/[\u200b-\u200d\ufeff]/g, "").replace(/\s+/g, " ").trim();
  const currentProvider = () => location.origin === "https://gemini.google.com" ? "gemini-web"
    : location.origin === "https://chatgpt.com" ? "chatgpt-web" : null;
  const visible = element => !!element?.isConnected && element.getClientRects().length > 0
    && getComputedStyle(element).visibility !== "hidden" && getComputedStyle(element).display !== "none";
  const enabled = element => visible(element) && !element.disabled && element.getAttribute("aria-disabled") !== "true";

  function list(selector, root = document) {
    return [...root.querySelectorAll(selector)].filter((element, index, array) => !array.some((other, otherIndex) => otherIndex !== index && other.contains(element)));
  }
  const folded = text => normal(text).normalize("NFD").replace(/[\u0300-\u036f]/g, "").replace(/đ/g, "d").toLowerCase();
  const composerSelector = 'rich-textarea, .text-input-field, [data-testid*="composer"], [data-type="unified-composer"], [class*="input-area"], [class*="chat-input"]';
  function editable(element) {
    return element instanceof HTMLTextAreaElement || element instanceof HTMLInputElement
      && ["text", ""].includes(element.type) || element?.isContentEditable;
  }
  function editingHost(element) {
    if (!element?.isContentEditable) return element;
    while (element.parentElement?.isContentEditable) element = element.parentElement;
    return element;
  }
  function labelOf(element) {
    return folded([element.getAttribute("aria-label"), element.getAttribute("placeholder"),
      element.getAttribute("data-placeholder"), element.getAttribute("title")].join(" "));
  }
  function inputCandidates(selectors) {
    const trusted = [...document.querySelectorAll(selectors.input)].map(editingHost);
    const elements = selectors.manualInput ? trusted : [...trusted,
      ...document.querySelectorAll('textarea, [contenteditable=""], [contenteditable="true"], [contenteditable="plaintext-only"], [role="textbox"]'),
      ...(pickedInput ? [pickedInput] : [])].map(editingHost);
    return [...new Set(elements)].map(element => {
      const label = labelOf(element);
      const rect = element.getBoundingClientRect();
      return {element, editable: editable(element), visible: visible(element), enabled: enabled(element) && !element.readOnly,
        excluded: !!element.closest('nav, aside, [role="search"], [role="dialog"], [aria-hidden="true"], [inert]')
          || /\b(search|tim kiem|filter|loc ket qua)\b/.test(label),
        trusted: trusted.includes(element), composer: !!element.closest(composerSelector), main: !!element.closest('main, [role="main"]'),
        semantic: /\b(prompt|message|ask|chat|hoi|tin nhan|nhap|gemini)\b/.test(label),
        multiline: element instanceof HTMLTextAreaElement || element.isContentEditable || element.getAttribute("aria-multiline") === "true",
        large: rect.width >= 160 && rect.height >= 24};
    });
  }
  function oneInput(selectors) {
    const candidates = inputCandidates(selectors);
    if (!selectors.manualInput && pickedInput) {
      const picked = candidates.find(row => row.element === pickedInput && row.editable && row.visible && row.enabled && !row.excluded);
      if (picked) return picked.element;
      pickedInput = null;
    }
    const chosen = discovery.input(candidates);
    if (!chosen) throw new Error("Chưa xác định chắc khung chat. Bấm Tự quét lại hoặc Chọn ô nhập trên trang trong extension.");
    return chosen.element;
  }
  function findSend(input, selectors, includeDisabled = false) {
    const trusted = [...document.querySelectorAll(selectors.send)];
    const elements = selectors.manualSend ? trusted : [...new Set([...trusted, ...document.querySelectorAll('button, [role="button"]')])];
    const rows = elements.map(element => {
      const icon = [...element.querySelectorAll('mat-icon, [data-icon]')].map(item =>
        [item.getAttribute("fonticon"), item.getAttribute("data-mat-icon-name"), item.getAttribute("data-icon"), item.textContent].join(" ")).join(" ");
      const label = labelOf(element) + " " + folded(element.textContent) + " " + folded(icon);
      return {element, visible: visible(element), enabled: includeDisabled ? true : enabled(element),
        excluded: element.matches(selectors.stop) || /\b(stop|cancel|dung|huy)\b/.test(label)
          || /\b(send feedback|gui y kien|share|chia se)\b/.test(label)
          || !!element.closest('nav, aside, [role="dialog"], [inert]'),
        trusted: trusted.includes(element), semantic: /\b(send|submit|gui)\b/.test(label)};
    });
    // Grow out of the editor until a send candidate is present; mic/attachment controls do not stop the search.
    let scope = input.closest('form') || input.closest(composerSelector) || input.parentElement;
    for (let depth = 0; scope && depth < 6; depth++) {
      if (rows.some(row => row.visible && !row.excluded && (row.trusted || row.semantic) && scope.contains(row.element))) break;
      if (!scope.parentElement || scope.parentElement.matches('main, body, [role="main"]')) break;
      scope = scope.parentElement;
    }
    for (const row of rows) row.local = !!scope?.contains(row.element);
    return discovery.send(rows.filter(row => row.trusted || row.local))?.element || null;
  }
  function modelLabel(provider) {
    const selector = provider === "chatgpt-web"
      ? '[data-testid="model-switcher-dropdown-button"], button[aria-label*="Model selector"], button[aria-label*="model picker"]'
      : '[data-test-id="bard-mode-menu-button"], [data-testid="model-selector"], button[aria-label*="model" i], button[aria-label*="mô hình" i], .model-selector, .input-area-switch';
    const labels = [...document.querySelectorAll(selector)].filter(visible).filter(element =>
      !element.closest('nav, aside, [role="dialog"], model-response, user-query, [data-message-author-role]'))
      .map(element => normal(element.innerText || element.getAttribute("aria-label")))
      .filter(text => text && text.length <= 120 && !/^(model|chọn model|mô hình|select model|open model selector)$/i.test(text));
    const unique = [...new Set(labels)];
    return unique.length === 1 ? unique[0] : "";
  }
  function scan(selectors) {
    let input;
    try { input = oneInput(selectors); }
    catch (error) { return {ok: false, error: error.message, model: modelLabel(currentProvider())}; }
    return {ok: true, model: modelLabel(currentProvider()), sendFound: !!findSend(input, selectors, true),
      draft: !!normal(inputText(input)), picked: input === pickedInput};
  }
  function startPicker() {
    if (active) throw new Error("Tab đang chạy. Không đổi khung nhập trong lượt đang chạy.");
    cancelPicker?.();
    const banner = document.createElement("div");
    banner.textContent = "Multi-Agent Hub · Bấm vào ô soạn chat để chọn. Esc để hủy. Không nhập hay gửi tin nhắn.";
    Object.assign(banner.style, {position: "fixed", top: "12px", left: "50%", transform: "translateX(-50%)", zIndex: "2147483647",
      background: "#143c2b", color: "white", padding: "14px 20px", borderRadius: "12px", font: "14px system-ui", pointerEvents: "none", maxWidth: "90vw"});
    document.documentElement.append(banner);
    let highlighted = null;
    let previousOutline = "", previousOffset = "";
    const clearHighlight = () => {
      if (highlighted) { highlighted.style.outline = previousOutline; highlighted.style.outlineOffset = previousOffset; }
      highlighted = null;
    };
    const targetInput = event => editingHost(event.target?.closest('textarea, input[type="text"], [contenteditable=""], [contenteditable="true"], [contenteditable="plaintext-only"], [role="textbox"]'));
    const move = event => {
      const target = targetInput(event);
      if (target === highlighted) return;
      clearHighlight();
      if (!target || !editable(target)) return;
      highlighted = target; previousOutline = target.style.outline; previousOffset = target.style.outlineOffset;
      target.style.outline = "3px solid #3489fa"; target.style.outlineOffset = "3px";
    };
    const finish = () => {
      clearTimeout(timer); clearHighlight(); banner.remove();
      document.removeEventListener("pointermove", move, true); document.removeEventListener("click", click, true);
      document.removeEventListener("keydown", key, true); cancelPicker = null;
    };
    const click = event => {
      event.preventDefault(); event.stopImmediatePropagation();
      const target = targetInput(event);
      if (!target || !editable(target) || !visible(target) || !enabled(target) || target.readOnly
          || target.closest('nav, aside, [role="search"], [role="dialog"], [inert]') || /\b(search|tim kiem|filter)\b/.test(labelOf(target))) return;
      pickedInput = target; finish();
      void chrome.runtime.sendMessage({type: "hub-picked", nonce, url: location.href}).catch(() => {});
    };
    const key = event => { if (event.key === "Escape") { event.preventDefault(); event.stopImmediatePropagation(); finish(); } };
    const timer = setTimeout(finish, 30000);
    cancelPicker = finish;
    document.addEventListener("pointermove", move, true); document.addEventListener("click", click, true); document.addEventListener("keydown", key, true);
  }
  function inputText(input) { return input.isContentEditable ? input.innerText : input.value; }
  function selected(custom, provider = currentProvider()) {
    if (!defaults[provider]) throw new Error("Trang này không được hỗ trợ.");
    const selectors = {...defaults[provider], ...custom};
    for (const value of Object.values(selectors)) document.querySelector(value); // Reject invalid CSS before changing the composer.
    Object.defineProperties(selectors, {manualInput: {value: !!custom?.input}, manualSend: {value: !!custom?.send}});
    return selectors;
  }
  function readText(element, preserveLinks = false) {
    const clone = element.cloneNode(true);
    clone.querySelectorAll('button, svg, nav, script, style, [aria-hidden="true"]').forEach(item => item.remove());
    if (preserveLinks) clone.querySelectorAll('a[href]').forEach(anchor => {
      try {
        const url = new URL(anchor.getAttribute("href"), location.href);
        if (!["https:", "http:"].includes(url.protocol) || url.username || url.password) return;
        const label = (anchor.textContent || "").trim();
        anchor.replaceWith(label === url.href ? label : `${label || "Nguồn"} (${url.href})`);
      } catch { /* Keep the visible text if a source link is malformed. */ }
    });
    clone.querySelectorAll("br").forEach(item => item.replaceWith("\n"));
    clone.querySelectorAll("p, div, li, h1, h2, h3, h4, pre, tr, blockquote").forEach(item => item.append("\n"));
    clone.querySelectorAll("td, th").forEach(item => item.append("\t"));
    return (clone.textContent || "").replace(/\n[ \t]+/g, "\n").replace(/\n{3,}/g, "\n\n").trim();
  }
  function responseText(assistant, selectors) {
    const parts = list(selectors.response, assistant);
    return parts.length ? parts.map(part => readText(part, true)).join("\n\n").trim() : readText(assistant, true);
  }
  function ownUser(request) {
    const users = list(request.selectors.user);
    if (users.length <= request.userCount) return null;
    const user = users[request.userCount];
    const actual = normal(readText(user));
    const expected = normal(request.prompt);
    if (!(actual === expected || actual.startsWith(expected) || actual.endsWith(expected))) return null;
    if (users.length > request.userCount + 1) throw new Error("Có thêm tin nhắn khác trong tab. Đã dừng cầu nối để tránh trộn cuộc trò chuyện.");
    return user;
  }
  function afterOwnUser(request) {
    const user = ownUser(request);
    if (!user) return null;
    const assistants = list(request.selectors.assistant).filter(item => user.compareDocumentPosition(item) & Node.DOCUMENT_POSITION_FOLLOWING);
    return assistants.at(-1) || null;
  }
  function isGenerating(selectors, assistant) {
    return list(selectors.stop).some(visible) || !!assistant?.matches(selectors.busy)
      || !!assistant && list(selectors.busy, assistant).some(visible);
  }
  function terminalActions(assistant, selectors) {
    const scope = assistant.closest('article, [data-testid^="conversation-turn-"], model-response, [data-test-id="model-response"]') || assistant;
    return list(selectors.actions, scope).some(enabled);
  }
  function emit(request, type, extra) {
    if (active !== request || request.cancelled) return;
    void chrome.runtime.sendMessage({type, id: request.id, nonce, url: location.href, ...extra}).catch(() => {
      request.cancelled = true;
    });
  }
  function fill(input, prompt) {
    input.focus();
    const event = new InputEvent("beforeinput", {bubbles: true, cancelable: true, inputType: "insertText", data: prompt});
    if (!input.dispatchEvent(event)) throw new Error("Website từ chối nhập văn bản. Chưa gửi yêu cầu.");
    if (input.isContentEditable) {
      const selection = getSelection();
      const range = document.createRange();
      range.selectNodeContents(input);
      selection.removeAllRanges(); selection.addRange(range);
      if (!document.execCommand("insertText", false, prompt)) throw new Error("Không nhập được yêu cầu vào website. Chưa gửi.");
    } else {
      const prototype = input instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
      Object.getOwnPropertyDescriptor(prototype, "value").set.call(input, prompt);
    }
    input.dispatchEvent(new InputEvent("input", {bubbles: true, inputType: "insertText", data: prompt}));
    input.dispatchEvent(new Event("change", {bubbles: true}));
  }
  async function run(request) {
    try {
      const selectors = request.selectors;
      const input = oneInput(selectors);
      if (normal(inputText(input))) throw new Error("Khung nhập đang có nội dung của bạn. Giữ nguyên nội dung và chưa gửi yêu cầu Hub.");
      if (isGenerating(selectors, null)) throw new Error("Website đang tạo phản hồi khác. Đợi hoàn tất hoặc dừng trực tiếp trong tab.");
      request.userCount = list(selectors.user).length;
      fill(input, request.prompt);
      let send;
      const deadline = Date.now() + 5000;
      while (Date.now() < deadline && !request.cancelled) {
        if (location.href !== request.url || !input.isConnected) throw new Error("Tab thay đổi trong lúc nhập. Kiểm tra tab trước khi gửi lại.");
        if (normal(inputText(input)) !== normal(request.prompt)) throw new Error("Nội dung khung nhập đã thay đổi. Chưa gửi yêu cầu Hub.");
        send = findSend(input, selectors);
        if (send) break;
        await sleep(100);
      }
      if (request.cancelled) return;
      if (!send) throw new Error("Không nhận diện được nút Gửi. Yêu cầu còn trong khung nhập; tiện ích không tự gửi lại.");
      request.submittedAt = Date.now();
      send.click(); // Only a verified send control; never synthesize a keyboard shortcut.
      let sawGenerating = false;
      let unrecognizedSince = 0;
      let terminalSince = 0;
      let lastText = "";
      let lastDelta = 0;
      while (!request.cancelled) {
        if (location.href !== request.url) {
          // Wait briefly for the background to approve only our initial conversation URL assignment.
          if (!request.navigationWait) request.navigationWait = Date.now();
          if (Date.now() - request.navigationWait > 3000) throw new Error("Tab đã đổi cuộc trò chuyện. Kiểm tra nội dung trước khi kết nối lại.");
          await sleep(100); continue;
        }
        request.navigationWait = 0;
        const assistant = afterOwnUser(request);
        const generating = isGenerating(selectors, assistant);
        if (generating) sawGenerating = true;
        const text = assistant ? responseText(assistant, selectors) : "";
        if (text.length > 64000) throw new Error("Phản hồi vượt 64.000 ký tự. Nội dung đầy đủ vẫn ở website; chưa chuyển sang lượt khác.");
        const changed = text !== lastText;
        if (changed) terminalSince = 0;
        if (changed && Date.now() - lastDelta >= 700) {
          lastText = text; lastDelta = Date.now();
          if (text) emit(request, "hub-delta", {text});
        }
        const terminal = assistant && text && sawGenerating && !generating && terminalActions(assistant, selectors);
        if (terminal) {
          if (!terminalSince) terminalSince = Date.now();
          // Stability only debounces an explicit terminal control; quiet text alone never completes a turn.
          if (Date.now() - terminalSince >= 800) {
            emit(request, "hub-result", {text}); return;
          }
        } else terminalSince = 0;
        if (!sawGenerating && Date.now() - request.submittedAt > 90000) {
          throw new Error("Không nhận diện được trạng thái website đang tạo phản hồi sau khi gửi. Kiểm tra tab/bộ chọn CSS; không tự gửi lại.");
        }
        if (sawGenerating && !generating && !terminal) {
          if (!unrecognizedSince) unrecognizedSince = Date.now();
          if (Date.now() - unrecognizedSince > 60000) {
            throw new Error("Website ngừng tạo nhưng chưa thấy dấu hiệu hoàn tất rõ ràng. Đã giữ phần trả lời; cần kiểm tra tab trước khi tiếp tục.");
          }
        } else unrecognizedSince = 0;
        await sleep(200);
      }
    } catch (error) {
      emit(request, "hub-error", {message: error.message || "Không thể tiếp tục thao tác website."});
    } finally {
      if (active === request) active = null;
    }
  }
  function initialUrl(value, provider) {
    const url = new URL(value);
    return provider === "gemini-web" ? /^\/app\/?$/.test(url.pathname) : url.pathname === "/";
  }
  function assignedUrl(value, provider) {
    const url = new URL(value);
    return provider === "gemini-web" ? /^\/app\/[^/]+\/?$/.test(url.pathname) : /^\/c\/[^/]+\/?$/.test(url.pathname);
  }
  chrome.runtime.onMessage.addListener((message, sender, reply) => {
    if (sender.id !== chrome.runtime.id || !message) return false;
    if (message.type === "hub-scan" || message.type === "hub-pick") {
      try {
        if (message.provider !== currentProvider()) throw new Error("Sai website chatbot.");
        if (message.type === "hub-pick") { startPicker(); reply({ok: true, nonce, url: location.href}); }
        else reply({...scan(selected(message.selectors)), nonce, url: location.href});
      } catch (error) { reply({ok: false, error: error.message}); }
      return false;
    }
    if (message.type === "hub-bind") {
      try {
        if (message.provider !== currentProvider()) throw new Error("Sai website chatbot.");
        const selectors = selected(message.selectors, message.provider);
        const input = oneInput(selectors);
        const original = defaults[message.provider];
        if (!initialUrl(location.href, message.provider) || list(selectors.user).length || list(selectors.assistant).length
            || list(original.user).length || list(original.assistant).length) {
          throw new Error("Mở chat mới trên website rồi kết nối; để tiếp tục chủ đề cũ dùng lịch sử Hub, bridge sẽ chuyển context sang tab mới. Lịch sử website được giữ nguyên.");
        }
        if (normal(inputText(input))) throw new Error("Khung nhập đang có nội dung của bạn. Nội dung được giữ nguyên; mở chat mới trống rồi kết nối.");
        if (isGenerating(selectors, null)) throw new Error("Website đang tạo phản hồi. Giữ nguyên chat đang chạy và mở một chat mới để kết nối.");
        reply({ready: true, nonce, url: location.href});
      } catch (error) { reply({ready: false, message: error.message}); }
      return false;
    }
    if (message.nonce !== nonce) { reply({accepted: false, message: "Tab đã tải lại. Cần kết nối lại."}); return false; }
    if (message.type === "hub-ping") { reply({nonce, url: location.href}); return false; }
    if (message.type === "hub-cancel") {
      if (active?.id === message.id) active.cancelled = true;
      reply({ok: true}); return false;
    }
    if (message.type === "hub-navigation") {
      const request = active;
      try {
        const allowed = request?.id === message.id && !request.navigationUsed && request.submittedAt
          && Date.now() - request.submittedAt < 20000 && request.url === message.from && location.href === message.to
          && new URL(message.from).origin === new URL(message.to).origin
          && initialUrl(message.from, currentProvider()) && assignedUrl(message.to, currentProvider()) && !!ownUser(request);
        if (allowed) { request.navigationUsed = true; request.url = message.to; }
        reply({allowed: !!allowed, nonce, url: location.href});
      } catch { reply({allowed: false, nonce, url: location.href}); }
      return false;
    }
    if (message.type !== "hub-request") return false;
    if (active) { reply({accepted: false, message: "Tab đang xử lý yêu cầu khác."}); return false; }
    if (message.url !== location.href || typeof message.id !== "string" || message.id.length > 128
        || typeof message.prompt !== "string" || !message.prompt.trim() || message.prompt.length > 200000) {
      reply({accepted: false, message: "Yêu cầu không hợp lệ hoặc tab đã đổi trang."}); return false;
    }
    try {
      cancelPicker?.();
      const request = {id: message.id, prompt: message.prompt, url: location.href,
        selectors: selected(message.selectors), cancelled: false};
      active = request;
      reply({accepted: true});
      void run(request);
    } catch (error) { reply({accepted: false, message: error.message}); }
    return false;
  });
})();
