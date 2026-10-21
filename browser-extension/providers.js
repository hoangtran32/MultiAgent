/* Website adapters describe UI, never a fixed catalog of models or accounts. */
((root) => {
  "use strict";
  const generic = {
    input: 'textarea, [contenteditable="true"], [contenteditable="plaintext-only"]',
    send: 'button[type="submit"], button[aria-label*="send" i], button[aria-label*="gửi" i]',
    user: '[data-message-author-role="user"], [data-role="user"]',
    assistant: '[data-message-author-role="assistant"], [data-role="assistant"]',
    response: '.markdown, [data-message-content]',
    stop: 'button[aria-label*="stop" i], button[aria-label*="dừng" i]',
    actions: 'button[aria-label*="copy" i], button[aria-label*="sao chép" i]',
    busy: '[aria-busy="true"], [data-is-streaming="true"]'
  };
  const adapters = [
    {id: "gemini-web", name: "Gemini", host: "gemini.google.com", path: /^(?:\/u\/\d+)?\/app(?:\/[^/]+)?\/?$/, fresh: /^(?:\/u\/\d+)?\/app\/?$/,
      selectors: {input: 'rich-textarea .ql-editor[contenteditable="true"], rich-textarea [contenteditable="true"], .text-input-field textarea',
        send: 'button.send-button, button[aria-label="Send message"], button[aria-label="Gửi tin nhắn"], button[aria-label="Send"]',
        user: 'user-query, [data-test-id="user-query"]', assistant: 'model-response, [data-test-id="model-response"]',
        response: 'message-content, .markdown, .model-response-text',
        stop: 'button.send-button.stop, button.send-button:has(mat-icon[data-mat-icon-name="stop"]), button.send-button:has(mat-icon[fonticon="stop"]), button[aria-label="Stop response"], button[aria-label="Stop generating"], button[aria-label="Stop"], button[aria-label="Dừng câu trả lời"], button[aria-label="Dừng phản hồi"]',
        actions: 'button[data-test-id="copy-button"], button[data-test-id="copy-response-button"], button.copy-button, button[aria-label="Copy response"], button[aria-label="Sao chép phản hồi"]'}},
    {id: "chatgpt-web", name: "ChatGPT", host: "chatgpt.com", path: /^\/(?:c\/[^/]+)?\/?$/, fresh: /^\/$/,
      selectors: {input: '#prompt-textarea, textarea[data-testid="prompt-textarea"]',
        send: 'button[data-testid="send-button"], button[aria-label="Send prompt"], button[aria-label="Send message"], button[aria-label="Gửi tin nhắn"]',
        user: '[data-message-author-role="user"]', assistant: '[data-message-author-role="assistant"]',
        stop: 'button[data-testid="stop-button"], button[aria-label="Stop streaming"], button[aria-label="Stop generating"], button[aria-label="Dừng tạo"]',
        actions: 'button[data-testid="copy-turn-action-button"], button[aria-label="Copy response"], button[aria-label="Sao chép"]'}}
  ];
  function site(value) {
    try {
      const url = new URL(value);
      if (url.protocol !== "https:" || url.username || url.password || url.port) return null;
      const known = adapters.find(a => a.host === url.hostname);
      if (known && !known.path.test(url.pathname)) return null;
      return known ? {...known, origin: url.origin} : {id: "browser-web", name: url.hostname, origin: url.origin, experimental: true};
    } catch { return null; }
  }
  const api = Object.freeze({site, providerOf: value => site(value)?.id || null,
    fresh: value => { const a = site(value); return !!a && (!a.fresh || a.fresh.test(new URL(value).pathname)); },
    selectors: id => ({...generic, ...adapters.find(a => a.id === id)?.selectors})});
  root.MultiAgentHubProviders = api;
  if (typeof module !== "undefined" && module.exports) module.exports = api;
})(globalThis);
