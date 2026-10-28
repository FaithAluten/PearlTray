import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import test from "node:test";
import { makeBrowserCollector } from "../src/browser.mjs";
import { readConfig, UPSTREAM } from "../src/core.mjs";
import data from "./fixtures/pearlusdt.json" with { type: "json" };

const config = readConfig({ API_TOKEN: "test-token-".repeat(4) });

function browserFixture({ statuses = [200], body = data, timeout = false } = {}) {
  const page = new EventEmitter();
  const frame = {};
  let launches = 0;
  let closes = 0;
  page.mainFrame = () => frame;
  page.setDefaultTimeout = () => {};
  page.url = () => UPSTREAM;
  page.goto = async () => {
    page.emit("response", response(statuses[0]));
  };
  const response = (status) => ({
    status: () => status,
    request: () => ({
      isNavigationRequest: () => true,
      frame: () => frame,
    }),
  });
  page.waitForFunction = async () => {
    if (timeout) throw Object.assign(new Error("JSON wait timed out"), { name: "TimeoutError" });
    for (const status of statuses.slice(1)) page.emit("response", response(status));
    return { dispose: async () => {} };
  };
  page.locator = () => ({ innerText: async () => JSON.stringify(body) });
  const chromium = {
    launchPersistentContext: async () => {
      launches += 1;
      return {
        pages: () => [page],
        close: async () => { closes += 1; },
      };
    },
  };
  return {
    chromium,
    page,
    counts: () => ({ launches, closes }),
  };
}

test("collector reuses its browser and accepts JSON after a challenge navigation", async () => {
  const fixture = browserFixture({ statuses: [403, 200] });
  const collector = makeBrowserCollector(fixture.chromium, config);
  assert.deepEqual(await collector.collect(), data);
  assert.deepEqual(await collector.collect(), data);
  assert.deepEqual(fixture.counts(), { launches: 1, closes: 0 });
  assert.equal(fixture.page.listenerCount("response"), 0);
  await collector.close();
  assert.equal(fixture.counts().closes, 1);
  await assert.rejects(collector.collect(), /closed/);
});

test("HTTP 403 is never accepted as a successful ticker and browser can restart", async () => {
  const fixture = browserFixture({ statuses: [403], timeout: true });
  const collector = makeBrowserCollector(fixture.chromium, config);
  await assert.rejects(collector.collect(), /HTTP 403/);
  await assert.rejects(collector.collect(), /HTTP 403/);
  assert.deepEqual(fixture.counts(), { launches: 2, closes: 2 });
  assert.equal(fixture.page.listenerCount("response"), 0);
  await collector.close();
});

test("reject an upstream error body even when HTTP status is 200", async () => {
  const fixture = browserFixture({ body: { error: "unavailable" } });
  const collector = makeBrowserCollector(fixture.chromium, config);
  await assert.rejects(collector.collect(), /expected ticker JSON/);
  assert.equal(fixture.counts().closes, 1);
  await collector.close();
});
