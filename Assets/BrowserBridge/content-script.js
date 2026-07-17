(function () {
  if (window.top !== window) {
    return;
  }

  const authenticatedPath = "/portal/service_center.html";
  const authenticatedMarkers = ["工作台", "搜索应用，或输入一个网址进行代理访问"];
  const authenticationMarkers = ["统一身份认证", "用户名", "密码", "登录"];
  let lastState = null;
  let lastReportAt = 0;

  function visibleText() {
    return document.body?.innerText ?? "";
  }

  function containsAny(text, markers) {
    return markers.some((marker) => text.includes(marker));
  }

  function classifyVisiblePortalState() {
    if (document.visibilityState !== "visible") {
      return "unknown";
    }

    const text = visibleText();
    const path = window.location.pathname;
    if (path === authenticatedPath && containsAny(text, authenticatedMarkers)) {
      return "authenticated";
    }

    if (path.toLowerCase().includes("login") || containsAny(text, authenticationMarkers)) {
      return "auth-required";
    }

    return "unknown";
  }

  function reportState() {
    const state = classifyVisiblePortalState();
    const now = Date.now();
    if (state === lastState && now - lastReportAt < 5000) {
      return;
    }

    lastState = state;
    lastReportAt = now;
    chrome.runtime.sendMessage({ state });
  }

  const observer = new MutationObserver(reportState);
  observer.observe(document.documentElement, { childList: true, subtree: true, characterData: true });
  window.addEventListener("hashchange", reportState);
  window.addEventListener("popstate", reportState);
  window.setInterval(reportState, 5000);
  reportState();
})();
