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

function postToNative(message) {
  const port = ensureNativePort();
  if (port === null) {
    return false;
  }

  try {
    port.postMessage(message);
    return true;
  } catch {
    nativePort = null;
    return false;
  }
}

function reportBridgeReady() {
  postToNative({ type: "browser-bridge-ready" });
}

chrome.runtime.onInstalled.addListener(reportBridgeReady);
chrome.runtime.onStartup.addListener(reportBridgeReady);
reportBridgeReady();

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

  postToNative({
    type: "browser-auth-state",
    state: message.state
  });
});
