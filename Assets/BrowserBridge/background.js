const nativeHostName = "com.cquautologin.browser_bridge";
const portalHostName = "atrust.cqu.edu.cn";
const validStates = new Set(["unknown", "auth-required", "authenticated"]);
let nativePort = null;

function ensureNativePort() {
  if (nativePort !== null) {
    return nativePort;
  }

  try {
    nativePort = chrome.runtime.connectNative(nativeHostName);
    nativePort.onDisconnect.addListener(() => {
      nativePort = null;
    });
  } catch {
    nativePort = null;
  }

  return nativePort;
}

chrome.runtime.onMessage.addListener((message, sender) => {
  if (!sender.tab?.url || typeof message?.state !== "string" || !validStates.has(message.state)) {
    return;
  }

  try {
    if (new URL(sender.tab.url).hostname !== portalHostName) {
      return;
    }
  } catch {
    return;
  }

  const port = ensureNativePort();
  if (port === null) {
    return;
  }

  try {
    port.postMessage({
      type: "browser-auth-state",
      state: message.state
    });
  } catch {
    nativePort = null;
  }
});
