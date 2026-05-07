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
Rate limit: 20 agent requests per client IP per minute
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
```

Do not set `AgentBlazor:LicenseKey` for the public v1 demo unless you intentionally want Pro surfaces visible.

## 5. Logging And Cost Guardrails

Current demo observability is intentionally lightweight:

- ASP.NET Core console logging is enabled at `Information` for `AgentBlazor` categories.
- Azure Container Apps can stream container `stdout`/`stderr` logs without adding Application Insights code.
- The Container Apps environment should use `--logs-destination none` to avoid persisted Azure Monitor / Log Analytics ingestion charges.
- AgentBlazor prompt tracing is enabled in memory for runtime debugging, but traces are not persisted across container restarts.
- The public agent endpoint is rate limited by client IP. The current deployment uses `20` requests per minute.

What is not wired yet:

- No Application Insights SDK or OpenTelemetry exporter is registered by the demo app.
- No durable structured chat-request log exists yet for prompt length, route, agent, timing, status, or error.
- No OpenAI token or cost usage tracker is stored by the demo app.

For a low-cost public demo, prefer structured console logs first and keep Log Analytics/Application Insights ingestion off or minimal unless there is a specific incident to debug.

## Rollback

Redeploy a known-good image tag:

```bash
export OPENAI_API_KEY="<live-demo-openai-key>"
export AGENTBLAZOR_DEMO_IMAGE="ghcr.io/ashpeterson/agentblazor-demo:<commit-sha>"

./scripts/deploy/azure-container-apps-demo.sh
```
