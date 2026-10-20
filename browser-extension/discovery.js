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
    input: rows => choose(rows.filter(row => row.editable), row =>
      (row.trusted ? 100 : 0) + (row.composer ? 40 : 0) + (row.semantic ? 45 : 0)
      + (row.main ? 15 : 0) + (row.multiline ? 10 : 0) + (row.large ? 5 : 0), 60, 25),
    send: rows => choose(rows.filter(row => row.trusted || row.semantic), row =>
      (row.trusted ? 80 : 0) + (row.semantic ? 70 : 0) + (row.local ? 100 : 0), 70, 30)
  };
  globalThis.MultiAgentHubDiscovery = Object.freeze(rules);
  if (typeof module !== "undefined") module.exports = rules;
})();
