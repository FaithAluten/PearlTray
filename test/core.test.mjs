import assert from "node:assert/strict";
import test from "node:test";
import { makeServer, newState, parseTicker, readConfig, snapshot, TICKER_PATH } from "../src/core.mjs";
import data from "./fixtures/pearlusdt.json" with { type: "json" };

const token = "test-token-".repeat(4);

test("accept ticker JSON and reject block pages, error JSON, and invalid prices", () => {
  assert.deepEqual(parseTicker(JSON.stringify(data)), data);
  for (const value of [
    "<html>Cloudflare</html>",
    '{"error":"blocked"}',
    'null',
    '[]',
    '{"last":""}',
    '{"last":null}',
    '{"last":"NaN"}',
    '{"last":-1}',
    '{"ticker":{"last":""}}',
    '{"ticker":{"last":null}}',
    '{"ticker":{"last":"NaN"}}',
  ]) {
    assert.throws(() => parseTicker(value));
  }
});

test("continue accepting a wrapped ticker without changing its response shape", () => {
  const wrapped = { at: 1792987200, ticker: { last: "0.0123", buy: "0.012" } };
  assert.deepEqual(parseTicker(JSON.stringify(wrapped)), wrapped);
});

test("configuration rejects missing credentials and invalid polling values", () => {
  assert.throws(() => readConfig({}));
  assert.throws(() => readConfig({ API_TOKEN: token, POLL_SECONDS: "0" }));
  assert.throws(() => readConfig({ API_TOKEN: token, HEADLESS: "maybe" }));
  assert.equal(readConfig({ API_TOKEN: token }).headless, false);
});

test("freshness expires cached data without deleting the last success", () => {
  const state = { ...newState(), data, updatedAt: 1000 };
  assert.equal(snapshot(state, 2000, 3000).ready, true);
  assert.equal(snapshot(state, 2000, 3001).ready, false);
  assert.equal(snapshot(newState(), 2000, 3000).ready, false);
});

test("HTTP authentication, original payload, failure metadata, and expiration", async () => {
  const config = readConfig({ API_TOKEN: token });
  const state = newState();
  const server = makeServer(config, state);
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  const base = `http://127.0.0.1:${server.address().port}`;
  const headers = { Authorization: `Bearer ${token}` };
  try {
    assert.equal((await fetch(`${base}/ticker`)).status, 401);
    assert.equal(
      (await fetch(`${base}/ticker`, { headers: { Authorization: "Bearer wrong" } })).status,
      401,
    );
    assert.equal((await fetch(`${base}/healthz`)).status, 503);
    assert.equal((await fetch(`${base}/ticker`, { headers })).status, 503);
    state.data = data;
    state.updatedAt = Date.now();
    state.lastError = "Upstream returned HTTP 403";
    const response = await fetch(`${base}${TICKER_PATH}`, { headers });
    assert.equal(response.status, 200);
    assert.deepEqual(await response.json(), data);
    assert.equal(response.headers.get("x-upstream-state"), "refresh-failed");
    assert.equal(response.headers.get("cache-control"), "no-store");
    assert.equal((await fetch(`${base}/healthz`)).status, 200);
    assert.equal((await (await fetch(`${base}/status`, { headers })).json()).lastError, state.lastError);
    assert.equal((await fetch(`${base}/ticker`, { method: "POST", headers })).status, 405);
    assert.equal((await fetch(`${base}/elsewhere`, { headers })).status, 404);
    state.updatedAt = Date.now() - config.maxAgeMs - 1000;
    const expired = await fetch(`${base}/ticker`, { headers });
    assert.equal(expired.status, 503);
    assert.equal((await expired.json()).error, "ticker_expired");
    assert.equal((await fetch(`${base}/healthz`)).status, 503);
  } finally {
    server.closeAllConnections();
    await new Promise((resolve) => server.close(resolve));
  }
});
