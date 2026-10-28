import { UPSTREAM, parseTicker } from "./core.mjs";

export function makeBrowserCollector(chromium, config) {
  let context;
  let page;
  let closed = false;

  const reset = async () => {
    const previous = context;
    context = undefined;
    page = undefined;
    await previous?.close().catch(() => {});
  };

  return {
    async collect() {
      if (closed) throw new Error("Collector is closed");
      try {
        if (!context) {
          context = await chromium.launchPersistentContext(config.profileDir, {
            headless: config.headless,
            viewport: { width: 1280, height: 800 },
            locale: "en-US",
            timeout: config.browserTimeoutMs,
            acceptDownloads: false,
          });
          if (closed) {
            await reset();
            throw new Error("Collector is closed");
          }
          page = context.pages()[0] || (await context.newPage());
          page.setDefaultTimeout(config.browserTimeoutMs);
        }
        const activePage = page;
        // A challenge may navigate again; track the final document's HTTP status.
        let documentStatus;
        const onResponse = (response) => {
          const request = response.request();
          if (
            request.isNavigationRequest() &&
            request.frame() === activePage.mainFrame()
          ) {
            documentStatus = response.status();
          }
        };
        activePage.on("response", onResponse);
        try {
          const deadline = Date.now() + config.browserTimeoutMs;
          await activePage.goto(UPSTREAM, {
            waitUntil: "domcontentloaded",
            timeout: Math.max(1, deadline - Date.now()),
          });
          // Wait for JSON only. No CAPTCHA solving or challenge manipulation.
          const ready = await activePage.waitForFunction(
            () => {
              try {
                const value = JSON.parse(document.body?.innerText || "");
                return value !== null && typeof value === "object";
              } catch {
                return false;
              }
            },
            undefined,
            { timeout: Math.max(1, deadline - Date.now()) },
          );
          await ready.dispose();
          if (documentStatus !== 200) {
            throw new Error(`Upstream returned HTTP ${documentStatus ?? "unknown"}`);
          }
          if (new URL(activePage.url()).origin !== new URL(UPSTREAM).origin) {
            throw new Error("Upstream redirected to a different origin");
          }
          return parseTicker(await activePage.locator("body").innerText());
        } catch (error) {
          if (documentStatus && documentStatus !== 200) {
            throw new Error(`Upstream returned HTTP ${documentStatus}; ${error.name}`);
          }
          throw error;
        } finally {
          activePage.off("response", onResponse);
        }
      } catch (error) {
        // Recreate crashed browsers while keeping cookies on disk.
        await reset();
        throw error;
      }
    },
    async close() {
      closed = true;
      await reset();
    },
  };
}
