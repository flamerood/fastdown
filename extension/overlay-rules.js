(() => {
  const limit = 200;
  function parsed(value) {
    try {
      const url = new URL(value);
      return /^https?:$/.test(url.protocol) && !url.username && !url.password && url.href.length <= 16384 ? url : null;
    } catch { return null; }
  }
  function pageKey(value) {
    const url = parsed(value);
    if (!url) return null;
    url.hash = "";
    url.searchParams.sort();
    return url.href;
  }
  function rule(scope, value) {
    const url = parsed(value);
    if (!url || !["page", "site"].includes(scope)) return null;
    return { scope, url: scope === "site" ? url.origin : pageKey(value) };
  }
  function normalize(values) {
    const unique = new Map();
    for (const value of (Array.isArray(values) ? values : []).slice(0, limit)) {
      const item = rule(value?.scope, value?.url);
      if (item) unique.set(item.scope + ":" + item.url, item);
    }
    return [...unique.values()];
  }
  function matches(rules, ...pages) {
    const keys = pages.map(value => ({ page: pageKey(value), site: parsed(value)?.origin }));
    return rules.some(item => keys.some(key => item.url === key[item.scope]));
  }
  globalThis.FastdownOverlayRules = Object.freeze({ pageKey, rule, normalize, matches, limit });
})();
