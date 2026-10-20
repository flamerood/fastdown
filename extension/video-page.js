(() => {
  function youtubeUrl(value) {
    try {
      const url = new URL(value);
      if (!/^https?:$/.test(url.protocol) || url.username || url.password) return null;
      const host = url.hostname.toLowerCase();
      let id;
      if (host === "youtu.be") id = url.pathname.split("/")[1];
      else if (host === "youtube.com" || host.endsWith(".youtube.com") || host === "youtube-nocookie.com" || host.endsWith(".youtube-nocookie.com")) {
        if (url.pathname === "/watch") id = url.searchParams.get("v");
        else if (/^\/(shorts|live|embed)\//.test(url.pathname)) id = url.pathname.split("/")[2];
      }
      return /^[a-zA-Z0-9_-]{11}$/.test(id || "") ? "https://www.youtube.com/watch?v=" + id : null;
    } catch { return null; }
  }
  globalThis.FastdownVideoPage = Object.freeze({ youtubeUrl });
})();
