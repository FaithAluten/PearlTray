import { chromium } from "playwright";
import { setTimeout as delay } from "node:timers/promises";
import { makeServer, newState, readConfig } from "./core.mjs";
import { makeBrowserCollector } from "./browser.mjs";

const config = readConfig();
const state = newState();
const server = makeServer(config, state);
const collector = makeBrowserCollector(chromium, config);
const stop = new AbortController();
const log = (event, details = {}) =>
  console.log(JSON.stringify({ time: new Date().toISOString(), event, ...details }));

await new Promise((resolve, reject) => {
  server.once("error", reject);
  server.listen(config.port, "0.0.0.0", resolve);
});
log("listening", { port: config.port });

let shuttingDown = false;
const shutdown = () => {
  if (shuttingDown) return;
  shuttingDown = true;
  log("stopping");
  stop.abort();
  server.close();
  void collector.close();
  const timer = setTimeout(() => process.exit(1), 25000);
  timer.unref();
};
process.on("SIGTERM", shutdown);
process.on("SIGINT", shutdown);

while (!stop.signal.aborted) {
  state.refreshing = true;
  state.lastAttemptAt = Date.now();
  try {
    const data = await collector.collect();
    state.data = data;
    state.updatedAt = Date.now();
    state.lastError = null;
    state.failures = 0;
    log("refreshed");
  } catch (error) {
    if (!stop.signal.aborted) {
      // Avoid logging browser dumps that may contain session details.
      state.lastError = error.message.split("\n")[0].slice(0, 240);
      state.failures += 1;
      log("refresh_failed", { error: state.lastError, failures: state.failures });
    }
  } finally {
    state.refreshing = false;
  }
  const interval = state.failures
    ? Math.min(
        config.pollMs * 2 ** Math.min(state.failures, 3),
        Math.max(config.pollMs, 300000),
      )
    : config.pollMs;
  await delay(interval, undefined, { signal: stop.signal }).catch((error) => {
    if (error.name !== "AbortError") throw error;
  });
}
await collector.close();
server.closeAllConnections();
