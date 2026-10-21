/* Shared confidence rules. No website text or account data leaves the tab. */
(() => {
  "use strict";
  function choose(rows, score, minimum, gap) {
    const ranked = rows.filter(row => row.visible && row.enabled && !row.excluded)
      .map(row => ({row, score: score(row)})).sort((a, b) => b.score - a.score);
    const best = ranked[0];
    if (!best || best.score < minimum || ranked[1] && best.score - ranked[1].score < gap) return null;
    return best.row;
  }
  const rules = {
    userMatch(actual, expected, evidence = {}) {
      const normal = text => String(text || "").normalize("NFC").replace(/[\u200b-\u200d\ufeff]/g, "").replace(/\s+/g, " ").trim();
      actual = normal(actual); expected = normal(expected);
      if (!actual || !expected || evidence.interfered || evidence.extraUsers !== 1) return "";
      if (actual === expected || actual.startsWith(expected) || actual.endsWith(expected)) return "exact";
      // Group prompts already carry a nonce. A visible matching nonce is stronger than a folded prefix.
      const marker = expected.match(/<\|hub_turn_[a-f0-9]{32}\|>/)?.[0];
      if (marker && actual.includes(marker)) return "marker";
      if (evidence.networkConfirmed) return "request";
      if (!evidence.submitted || !evidence.editorCleared || !evidence.collapsed) return "";
      const prefix = actual.replace(/\s*(?:xem th[eê]m|hiển thị thêm|show more|read more|expand)(?:\s*[⌄∨v])?\s*$/iu, "")
        .replace(/\s*(?:\.{3}|…)\s*$/u, "").trim();
      return prefix.length >= 160 && prefix.length < expected.length && expected.startsWith(prefix) ? "folded" : "";
    },
    input: rows => choose(rows.filter(row => row.editable), row =>
      (row.trusted ? 100 : 0) + (row.composer ? 40 : 0) + (row.semantic ? 45 : 0)
      + (row.main ? 15 : 0) + (row.multiline ? 10 : 0) + (row.large ? 5 : 0), 60, 25),
    send: rows => choose(rows.filter(row => row.trusted || row.semantic), row =>
      (row.trusted ? 80 : 0) + (row.semantic ? 70 : 0) + (row.local ? 100 : 0), 70, 30)
  };
  globalThis.MultiAgentHubDiscovery = Object.freeze(rules);
  if (typeof module !== "undefined") module.exports = rules;
})();
