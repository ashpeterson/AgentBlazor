# Deploy The Demo

Recommended low-cost host: Azure Container Apps Consumption, using a public GHCR image. This keeps the demo on a scale-to-zero container platform, avoids an Azure Container Registry monthly charge, supports WebSockets for Blazor Server, and supports managed TLS for a custom subdomain.

Use `demo.agentblazor.com` for the live demo. Keep `agentblazor.com` free for the landing page.

Live URL:

- https://demo.agentblazor.com/demo/workflows/support-inbox

## 1. Build The Image

Run the `Demo Container` GitHub Actions workflow. It publishes:

```text
ghcr.io/ashpeterson/agentblazor-demo:latest
ghcr.io/ashpeterson/agentblazor-demo:<commit-sha>
```

If the package is private after the first run, make it public in GitHub Packages before deploying to Azure Container Apps.

## 2. Deploy To Azure

Prerequisites:

```bash
az login
az account set --subscription "<subscription-id-or-name>"
```

Deploy:

```bash
export OPENAI_API_KEY="<live-demo-openai-key>"
export AGENTBLAZOR_DEMO_IMAGE="ghcr.io/ashpeterson/agentblazor-demo:latest"
export DEMO_LOG_ACCESS_TOKEN="<long-random-token>"

./scripts/deploy/azure-container-apps-demo.sh
```

The script creates or updates:

```text
Resource group: agentblazor-demo-rg
Container Apps environment: agentblazor-demo-env
Container app: agentblazor-demo
Ingress: external, target port 8080
Scale: 0-2 replicas
Secret: openai-api-key
Optional secret: demo-log-access-token
Rate limit: 20 agent requests per client IP per minute
Demo request log: /tmp/agentblazor-demo-logs/chat-requests.jsonl
```

The script prints the generated Azure hostname. Smoke test it before adding DNS:

```bash
curl -I "https://<generated-hostname>"
```

## 3. Add The Domain

Start with `demo.agentblazor.com`, not the apex domain.

In the Azure portal:

1. Open `agentblazor-demo`.
2. Go to `Settings` -> `Custom domains`.
3. Add `demo.agentblazor.com`.
4. Choose `Managed certificate`.
5. Add the DNS records Azure gives you at your domain registrar.

For a subdomain, Azure Container Apps expects a direct CNAME to the generated Container Apps hostname. If the domain is on Cloudflare, set the record to DNS-only while Azure issues the managed certificate.

## 4. Production Settings

Keep these enabled for the public demo:

```text
ASPNETCORE_ENVIRONMENT=Production
OPENAI_API_KEY=secretref:openai-api-key
OpenAI__Model=gpt-4o-mini
DemoSecurity__TrustForwardedHeaders=true
DemoSecurity__RateLimiting__PermitLimit=20
DemoSecurity__RateLimiting__WindowSeconds=60
DemoLogging__DirectoryPath=/tmp/agentblazor-demo-logs
DemoLogging__AccessToken=secretref:demo-log-access-token
```

Do not set `AgentBlazor:LicenseKey` for the public v1 demo unless you intentionally want Pro surfaces visible.

## 5. Logging And Cost Guardrails

Current demo observability is intentionally lightweight:

- ASP.NET Core console logging is enabled at `Information` for `AgentBlazor` categories.
- Azure Container Apps can stream container `stdout`/`stderr` logs without adding Application Insights code.
- The Container Apps environment should use `--logs-destination none` to avoid persisted Azure Monitor / Log Analytics ingestion charges.
- Each page view and agent turn is appended to local JSONL files inside the container.
- AgentBlazor prompt tracing is enabled in memory for runtime debugging, but traces are not persisted across container restarts.
- The public agent endpoint is rate limited by client IP. The current deployment uses `20` requests per minute.

The chat JSONL file records:

- timestamp, request id, route, agent, hashed session id
- prompt length, response length, duration, outcome, error type
- approval/clarification flags and execution counts

The traffic JSONL file records:

- timestamp, request id, route path, method, status code, duration
- hashed visitor fingerprint, hashed user agent, referrer host

It does not record full prompt text, raw IP addresses, or raw user agents unless `DemoLogging__IncludePromptPreview=true` is explicitly set for chat prompt previews.

Get the traffic and chat summary:

```bash
curl -H "X-Demo-Log-Token: $DEMO_LOG_ACCESS_TOKEN" \
  "https://demo.agentblazor.com/internal/demo-logs/summary"
```

Access recent chat log lines:

```bash
curl -H "X-Demo-Log-Token: $DEMO_LOG_ACCESS_TOKEN" \
  "https://demo.agentblazor.com/internal/demo-logs?lines=200"
```

Access recent traffic log lines:

```bash
curl -H "X-Demo-Log-Token: $DEMO_LOG_ACCESS_TOKEN" \
  "https://demo.agentblazor.com/internal/demo-logs/traffic?lines=200"
```

Download the current chat file:

```bash
curl -H "X-Demo-Log-Token: $DEMO_LOG_ACCESS_TOKEN" \
  -o agentblazor-demo-chat-requests.jsonl \
  "https://demo.agentblazor.com/internal/demo-logs/download"
```

Download the current traffic file:

```bash
curl -H "X-Demo-Log-Token: $DEMO_LOG_ACCESS_TOKEN" \
  -o agentblazor-demo-traffic-requests.jsonl \
  "https://demo.agentblazor.com/internal/demo-logs/traffic/download"
```

What is not wired yet:

- No Application Insights SDK or OpenTelemetry exporter is registered by the demo app.
- No OpenAI token or cost usage tracker is stored by the demo app.

For a low-cost public demo, prefer structured console logs first and keep Log Analytics/Application Insights ingestion off or minimal unless there is a specific incident to debug.

## Rollback

Redeploy a known-good image tag:

```bash
export OPENAI_API_KEY="<live-demo-openai-key>"
export AGENTBLAZOR_DEMO_IMAGE="ghcr.io/ashpeterson/agentblazor-demo:<commit-sha>"

./scripts/deploy/azure-container-apps-demo.sh
```
