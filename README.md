# SafeTrade ticker on Debian

Windows 托盘程序位于 `windows-tray/`，直接通过系统 `curl.exe` 获取 SafeTrade
公开行情，无需价格 URL、API Token 或下述 Debian 中转服务。
双击 `windows-tray/dist/PearlTray.exe` 即可运行；
免安装包为 `windows-tray/PearlTray-windows.zip`。
详细说明见 `windows-tray/README.md`。

A persistent Chromium browser reads the public `pearlusdt` ticker. A small HTTP
service returns its latest successful JSON response. No browser is launched for
each API request. The browser runs with a virtual display by default.

Local browser success does not establish that a VPS IP will be accepted.
Cloudflare may still block this browser. This service does not solve CAPTCHAs.
A failed refresh is reported in `/status`; data older than 180 seconds is
unavailable with HTTP 503.

## Deploy

Requires a Debian VPS with Docker Engine and the Docker Compose plugin.
If Docker is missing, use the official Debian installation instructions:
https://docs.docker.com/engine/install/debian/

Copy this project to `/opt/safetrade-ticker`, then run there:
The external Docker network `reverse-network` must already exist, and Nginx
Proxy Manager must also be connected to it. Create the network with
`docker network create reverse-network` if it does not exist.

```sh
umask 077
cp .env.example .env
sed -i "s/^API_TOKEN=$/API_TOKEN=$(openssl rand -hex 32)/" .env
docker compose up -d --build
docker compose logs -f --tail=50 ticker
```

Allow about 60-90 seconds for the first attempt. Success logs contain
`"event":"refreshed"`. Inspect readiness on the VPS:

```sh
docker compose exec -T ticker node -e "fetch('http://127.0.0.1:8787/healthz').then(async r => console.log(r.status, await r.text()))"
```

To test the authenticated endpoint from inside the container:

```sh
docker compose exec -T ticker node -e 'fetch("http://127.0.0.1:8787/ticker",{headers:{Authorization:"Bearer "+process.env.API_TOKEN}}).then(async r=>console.log(r.status,await r.text()))'
```

The original path `/api/v2/trade/public/tickers/pearlusdt` is also supported.
Successful responses preserve the upstream JSON and include
`X-Data-Updated-At`, `X-Data-Age-Seconds`, and `X-Upstream-State` headers.
If the most recent refresh failed but the cached sample is still within
`MAX_AGE_SECONDS`, the response remains HTTP 200 with
`X-Upstream-State: refresh-failed`.

## Nginx Proxy Manager

The service does not publish a host port. In Nginx Proxy Manager, create a
Proxy Host with these settings:

- Domain Names: your API domain
- Scheme: `http`
- Forward Hostname / IP: `safetrade-ticker`
- Forward Port: `8787`
- Cache Assets: disabled

Point the domain to your VPS and configure its HTTPS certificate in Nginx
Proxy Manager. Requests should include `Authorization: Bearer YOUR_API_TOKEN`:

```sh
curl -H "Authorization: Bearer YOUR_API_TOKEN" https://your-api-domain/ticker
```

Keep any existing networks of the Nginx Proxy Manager service, and add
`reverse-network` to its Compose configuration as an external network.
Its upstream is the Docker network alias `safetrade-ticker`, not `127.0.0.1`.
The container still needs outbound internet connectivity to contact SafeTrade.

## Operations

```sh
docker compose ps
docker compose logs --tail=100 ticker
docker compose restart ticker
docker compose down
```

The `browser-data` volume persists cookies and browser storage across
container restarts. Keep it private. Container restarts clear the in-memory
ticker cache and return 503 until a new sample is collected.

Polling defaults to 60 seconds, with a minimum of 15. Failures back off
exponentially up to 5 minutes; there are no concurrent collection attempts.
`BROWSER_TIMEOUT_SECONDS` limits each navigation and JSON wait.
`MAX_AGE_SECONDS` controls the maximum accepted age of successful data.
Set these in `.env` and run `docker compose up -d` to apply.
If the maximum age is shorter than the polling interval, the API can
temporarily return 503 between otherwise successful updates.

If logs show HTTP 403 or a timeout, the VPS browser has not obtained a
valid ticker. A virtual display does not guarantee acceptance. At that
point, inspect access on that VPS and request an API access method from
SafeTrade if its protection continues blocking automated access.

## Local checks

The HTTP and validation tests use only Node's built-in modules:

```sh
node --test
```

Run the actual browser locally with `npm ci`, a Chromium installation
(`npx playwright install chromium`), `API_TOKEN`, `PROFILE_DIR`, and a display
or `HEADLESS=true`. The production Docker image already includes Chromium.
