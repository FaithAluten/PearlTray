import { createHash, timingSafeEqual } from "node:crypto";
import { createServer } from "node:http";

export const UPSTREAM =
  "https://safetrade.com/api/v2/trade/public/tickers/pearlusdt";
export const TICKER_PATH = new URL(UPSTREAM).pathname;

export function readConfig(env = process.env) {
  const integer = (name, fallback, min, max) => {
    const value = Number(env[name] ?? fallback);
    if (!Number.isInteger(value) || value < min || value > max) {
      throw new Error(`${name} must be an integer between ${min} and ${max}`);
    }
    return value;
  };
  const token = env.API_TOKEN?.trim();
  if (!token || token.length < 32) {
    throw new Error("API_TOKEN must contain at least 32 characters");
  }
  if (env.HEADLESS && !["true", "false"].includes(env.HEADLESS)) {
    throw new Error("HEADLESS must be true or false");
  }
  return {
    token,
    port: integer("PORT", 8787, 1, 65535),
    pollMs: integer("POLL_SECONDS", 60, 15, 3600) * 1000,
    maxAgeMs: integer("MAX_AGE_SECONDS", 180, 15, 86400) * 1000,
    browserTimeoutMs: integer("BROWSER_TIMEOUT_SECONDS", 60, 5, 300) * 1000,
    headless: env.HEADLESS === "true",
    profileDir: env.PROFILE_DIR || "/data/profile",
  };
}

export function parseTicker(text) {
  const value = JSON.parse(text);
  const ticker = value?.ticker ?? value;
  if (
    !ticker ||
    typeof ticker !== "object" ||
    Array.isArray(ticker) ||
    !["number", "string"].includes(typeof ticker.last) ||
    String(ticker.last).trim() === "" ||
    !Number.isFinite(Number(ticker.last)) ||
    Number(ticker.last) < 0
  ) {
    throw new Error("Upstream response is not the expected ticker JSON");
  }
  return value;
}

export function newState() {
  return {
    data: null,
    updatedAt: null,
    lastAttemptAt: null,
    lastError: null,
    failures: 0,
    refreshing: false,
  };
}

export function snapshot(state, maxAgeMs, now = Date.now()) {
  const ageMs = state.updatedAt === null ? null : Math.max(0, now - state.updatedAt);
  return {
    ready: state.data !== null && ageMs !== null && ageMs <= maxAgeMs,
    updatedAt: state.updatedAt === null ? null : new Date(state.updatedAt).toISOString(),
    ageSeconds: ageMs === null ? null : Math.floor(ageMs / 1000),
    lastAttemptAt:
      state.lastAttemptAt === null ? null : new Date(state.lastAttemptAt).toISOString(),
    lastError: state.lastError,
    consecutiveFailures: state.failures,
    refreshing: state.refreshing,
  };
}

export function makeServer(config, state) {
  const digest = (value) => createHash("sha256").update(value).digest();
  const expected = digest(`Bearer ${config.token}`);
  return createServer((req, res) => {
    res.setHeader("Content-Type", "application/json; charset=utf-8");
    res.setHeader("Cache-Control", "no-store");
    res.setHeader("X-Content-Type-Options", "nosniff");
    const send = (status, value) => {
      res.writeHead(status);
      res.end(JSON.stringify(value));
    };
    const path = (req.url || "/").split("?")[0];
    const status = snapshot(state, config.maxAgeMs);
    if (path === "/healthz" && req.method === "GET") {
      return send(status.ready ? 200 : 503, { ready: status.ready });
    }
    if (!timingSafeEqual(digest(req.headers.authorization || ""), expected)) {
      res.setHeader("WWW-Authenticate", "Bearer");
      return send(401, { error: "unauthorized" });
    }
    if (req.method !== "GET") {
      res.setHeader("Allow", "GET");
      return send(405, { error: "method_not_allowed" });
    }
    if (path === "/status") {
      return send(200, status);
    }
    if (path !== "/ticker" && path !== TICKER_PATH) {
      return send(404, { error: "not_found" });
    }
    if (!status.ready) {
      res.setHeader("Retry-After", String(Math.ceil(config.pollMs / 1000)));
      return send(503, {
        error: state.data === null ? "ticker_unavailable" : "ticker_expired",
        updatedAt: status.updatedAt,
        ageSeconds: status.ageSeconds,
      });
    }
    res.setHeader("X-Data-Updated-At", status.updatedAt);
    res.setHeader("X-Data-Age-Seconds", String(status.ageSeconds));
    res.setHeader("X-Upstream-State", state.lastError ? "refresh-failed" : "ok");
    return send(200, state.data);
  });
}
