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

window.bidManage.createPreviewUrlFromBase64 = function (fileName, contentType, base64Content) {
  if (!base64Content) {
    return "";
  }

  const binaryString = atob(base64Content);
  const len = binaryString.length;
  const bytes = new Uint8Array(len);
  for (let i = 0; i < len; i++) {
    bytes[i] = binaryString.charCodeAt(i);
  }

  const normalizedContentType = (contentType || "").toLowerCase();
  const normalizedFileName = (fileName || "").toLowerCase();
  const isExcel =
    normalizedContentType.includes("spreadsheetml.sheet") ||
    normalizedContentType.includes("application/vnd.ms-excel") ||
    normalizedFileName.endsWith(".xlsx") ||
    normalizedFileName.endsWith(".xls");

  if (isExcel && window.XLSX) {
    const workbook = window.XLSX.read(bytes, { type: "array" });
    const sections = workbook.SheetNames.map(function (sheetName) {
      const sheet = workbook.Sheets[sheetName];
      const tableHtml = window.XLSX.utils.sheet_to_html(sheet, {
        editable: false,
        header: "",
        footer: ""
      });

      return (
        '<section class="excel-preview__sheet">' +
          '<h2 class="excel-preview__sheet-title">' + escapeHtml(sheetName) + "</h2>" +
          '<div class="excel-preview__table-wrap">' + tableHtml + "</div>" +
        "</section>"
      );
    }).join("");

    const html = [
      "<!DOCTYPE html>",
      '<html lang="en">',
      "<head>",
      '<meta charset="utf-8" />',
      '<meta name="viewport" content="width=device-width, initial-scale=1" />',
      "<title>Excel preview</title>",
      "<style>",
      "body{margin:0;padding:24px;font-family:Poppins,Arial,sans-serif;background:#f5f7fb;color:#1f2937;}",
      ".excel-preview__sheet{margin-bottom:32px;}",
      ".excel-preview__sheet-title{margin:0 0 12px;font-size:18px;font-weight:600;}",
      ".excel-preview__table-wrap{overflow:auto;background:#fff;border:1px solid #d7deea;border-radius:12px;padding:12px;box-shadow:0 8px 24px rgba(15,23,42,.06);}",
      "table{border-collapse:collapse;width:max-content;min-width:100%;font-size:14px;}",
      "th,td{border:1px solid #d7deea;padding:8px 10px;vertical-align:top;text-align:left;white-space:pre-wrap;}",
      "th{background:#edf2ff;font-weight:600;}",
      "tr:nth-child(even) td{background:#fafbff;}",
      "</style>",
      "</head>",
      "<body>",
      sections || "<p>No worksheet data available.</p>",
      "</body>",
      "</html>"
    ].join("");

    const htmlBlob = new Blob([html], { type: "text/html" });
    return URL.createObjectURL(htmlBlob);
  }

  const blob = new Blob([bytes], { type: contentType || "application/octet-stream" });
  return URL.createObjectURL(blob);
};

window.bidManage.createBlobUrlFromBase64 = window.bidManage.createPreviewUrlFromBase64;

window.bidManage.revokeBlobUrl = function (url) {
  if (!url) {
    return;
  }

  URL.revokeObjectURL(url);
};

function escapeHtml(value) {
  return String(value)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

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
