window.bidManage = window.bidManage || {};

window.bidManage.getTextAreaSelectionById = function (elementId) {
  const textarea = document.getElementById(elementId);
  if (!textarea || typeof textarea.selectionStart !== "number" || typeof textarea.selectionEnd !== "number") {
    return null;
  }

  const startIndex = textarea.selectionStart;
  const endIndex = textarea.selectionEnd;
  const selectedText =
    startIndex === endIndex ? "" : textarea.value.substring(startIndex, endIndex);

  return {
    startIndex: startIndex,
    endIndex: endIndex,
    selectedText: selectedText
  };
};

window.bidManage.scrollToElementById = function (elementId) {
  const element = document.getElementById(elementId);
  if (!element) {
    return false;
  }

  element.scrollIntoView({ behavior: "smooth", block: "center", inline: "nearest" });
  return true;
};

window.bidManage._outsideClickHandlers = window.bidManage._outsideClickHandlers || {};

window.bidManage.registerOutsideClick = function (dotNetRef, rootElementId, registrationKey) {
  if (!dotNetRef || !rootElementId || !registrationKey) {
    return;
  }

  window.bidManage.unregisterOutsideClick(registrationKey);

  const handler = function (event) {
    const root = document.getElementById(rootElementId);
    if (!root) {
      return;
    }

    if (root.contains(event.target)) {
      return;
    }

    dotNetRef.invokeMethodAsync("OnOutsideClick");
  };

  window.bidManage._outsideClickHandlers[registrationKey] = handler;
  document.addEventListener("click", handler, true);
};

window.bidManage.unregisterOutsideClick = function (registrationKey) {
  if (!registrationKey) {
    return;
  }

  const handler = window.bidManage._outsideClickHandlers[registrationKey];
  if (!handler) {
    return;
  }

  document.removeEventListener("click", handler, true);
  delete window.bidManage._outsideClickHandlers[registrationKey];
};

window.bidManage.downloadFileFromBase64 = function (fileName, contentType, base64Content) {
  if (!fileName || !base64Content) {
    return;
  }

  const binaryString = atob(base64Content);
  const len = binaryString.length;
  const bytes = new Uint8Array(len);
  for (let i = 0; i < len; i++) {
    bytes[i] = binaryString.charCodeAt(i);
  }

  const blob = new Blob([bytes], { type: contentType || "application/octet-stream" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(url);
};

window.bidManage.createBlobUrlFromBase64 = function (contentType, base64Content) {
  if (!base64Content) {
    return "";
  }

  const binaryString = atob(base64Content);
  const len = binaryString.length;
  const bytes = new Uint8Array(len);
  for (let i = 0; i < len; i++) {
    bytes[i] = binaryString.charCodeAt(i);
  }

  const blob = new Blob([bytes], { type: contentType || "application/octet-stream" });
  return URL.createObjectURL(blob);
};

window.bidManage.revokeBlobUrl = function (url) {
  if (!url) {
    return;
  }

  URL.revokeObjectURL(url);
};

window.bidManage._hasUnsavedChanges = false;
window.bidManage._beforeUnloadHandler = function (event) {
  if (!window.bidManage._hasUnsavedChanges) {
    return;
  }

  event.preventDefault();
  event.returnValue = "";
};

window.bidManage.setUnsavedChangesGuard = function (enabled) {
  const shouldEnable = !!enabled;
  if (window.bidManage._hasUnsavedChanges === shouldEnable) {
    return;
  }

  window.bidManage._hasUnsavedChanges = shouldEnable;

  if (shouldEnable) {
    window.addEventListener("beforeunload", window.bidManage._beforeUnloadHandler);
    return;
  }

  window.removeEventListener("beforeunload", window.bidManage._beforeUnloadHandler);
};

window.bidManage.confirmDiscardUnsavedChanges = function () {
  return window.confirm("You have unsaved changes. Leave this page and discard them?");
};
