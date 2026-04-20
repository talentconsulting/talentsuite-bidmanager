(function () {
  const sessions = new Map();

  function getHeaders(accessToken) {
    const headers = {
      Accept: "application/x-ndjson"
    };

    if (accessToken) {
      headers.Authorization = `Bearer ${accessToken}`;
    }

    return headers;
  }

  async function streamResponse(response, dotNetRef) {
    if (!response.ok) {
      const text = await response.text();
      throw new Error(text || `Streaming request failed with HTTP ${response.status}.`);
    }

    const contentType = response.headers.get("content-type") || "";
    if (!contentType.toLowerCase().includes("application/x-ndjson")) {
      const text = await response.text();
      throw new Error(text || `Streaming request returned unexpected content type: ${contentType}.`);
    }

    if (!response.body) {
      throw new Error("Streaming response body was empty.");
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = "";

    while (true) {
      const result = await reader.read();
      if (result.done) {
        break;
      }

      buffer += decoder.decode(result.value, { stream: true });
      const lines = buffer.split("\n");
      buffer = lines.pop() || "";

      for (const line of lines) {
        const trimmed = line.trim();
        if (!trimmed) {
          continue;
        }

        await dotNetRef.invokeMethodAsync("HandleIngestionEvent", trimmed);
      }
    }

    const remainder = buffer.trim();
    if (remainder) {
      await dotNetRef.invokeMethodAsync("HandleIngestionEvent", remainder);
    }

    await dotNetRef.invokeMethodAsync("HandleIngestionStreamCompleted");
  }

  window.talentSuiteDocumentIngestion = {
    start: function (url, accessToken, dotNetRef) {
      const sessionId = crypto.randomUUID();
      const controller = new AbortController();
      sessions.set(sessionId, controller);

      fetch(url, {
        method: "GET",
        headers: getHeaders(accessToken),
        signal: controller.signal
      })
        .then(response => streamResponse(response, dotNetRef))
        .catch(async error => {
          if (controller.signal.aborted) {
            return;
          }

          await dotNetRef.invokeMethodAsync("HandleIngestionStreamError", error.message || "Streaming failed.");
        })
        .finally(() => {
          sessions.delete(sessionId);
        });

      return sessionId;
    },

    cancel: function (sessionId) {
      const controller = sessions.get(sessionId);
      if (!controller) {
        return;
      }

      controller.abort();
      sessions.delete(sessionId);
    }
  };
})();
