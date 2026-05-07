#!/usr/bin/env bash
set -euo pipefail

: "${OPENAI_API_KEY:?Set OPENAI_API_KEY before deploying.}"
: "${AGENTBLAZOR_DEMO_IMAGE:?Set AGENTBLAZOR_DEMO_IMAGE, for example ghcr.io/ashpeterson/agentblazor-demo:latest.}"

RESOURCE_GROUP="${RESOURCE_GROUP:-agentblazor-demo-rg}"
LOCATION="${LOCATION:-uksouth}"
ENVIRONMENT_NAME="${ENVIRONMENT_NAME:-agentblazor-demo-env}"
CONTAINER_APP_NAME="${CONTAINER_APP_NAME:-agentblazor-demo}"
OPENAI_MODEL="${OPENAI_MODEL:-gpt-4o-mini}"
RATE_LIMIT_PER_MINUTE="${RATE_LIMIT_PER_MINUTE:-20}"
DEMO_LOG_DIRECTORY="${DEMO_LOG_DIRECTORY:-/tmp/agentblazor-demo-logs}"

az extension add --name containerapp --upgrade >/dev/null

az group create \
  --name "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --output none

if ! az containerapp env show --name "$ENVIRONMENT_NAME" --resource-group "$RESOURCE_GROUP" >/dev/null 2>&1; then
  az containerapp env create \
    --name "$ENVIRONMENT_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --location "$LOCATION" \
    --output none
fi

if az containerapp show --name "$CONTAINER_APP_NAME" --resource-group "$RESOURCE_GROUP" >/dev/null 2>&1; then
  az containerapp secret set \
    --name "$CONTAINER_APP_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --secrets openai-api-key="$OPENAI_API_KEY" \
    --output none

  if [[ -n "${DEMO_LOG_ACCESS_TOKEN:-}" ]]; then
    az containerapp secret set \
      --name "$CONTAINER_APP_NAME" \
      --resource-group "$RESOURCE_GROUP" \
      --secrets demo-log-access-token="$DEMO_LOG_ACCESS_TOKEN" \
      --output none
  fi

  env_vars=(
    ASPNETCORE_ENVIRONMENT=Production
    OPENAI_API_KEY=secretref:openai-api-key
    OpenAI__Model="$OPENAI_MODEL"
    DemoSecurity__TrustForwardedHeaders=true
    DemoSecurity__RateLimiting__PermitLimit="$RATE_LIMIT_PER_MINUTE"
    DemoSecurity__RateLimiting__WindowSeconds=60
    DemoLogging__DirectoryPath="$DEMO_LOG_DIRECTORY"
  )

  if [[ -n "${DEMO_LOG_ACCESS_TOKEN:-}" ]]; then
    env_vars+=(DemoLogging__AccessToken=secretref:demo-log-access-token)
  fi

  az containerapp update \
    --name "$CONTAINER_APP_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --image "$AGENTBLAZOR_DEMO_IMAGE" \
    --set-env-vars "${env_vars[@]}" \
    --output none
else
  env_vars=(
    ASPNETCORE_ENVIRONMENT=Production
    OPENAI_API_KEY=secretref:openai-api-key
    OpenAI__Model="$OPENAI_MODEL"
    DemoSecurity__TrustForwardedHeaders=true
    DemoSecurity__RateLimiting__PermitLimit="$RATE_LIMIT_PER_MINUTE"
    DemoSecurity__RateLimiting__WindowSeconds=60
    DemoLogging__DirectoryPath="$DEMO_LOG_DIRECTORY"
  )

  secrets=(openai-api-key="$OPENAI_API_KEY")

  if [[ -n "${DEMO_LOG_ACCESS_TOKEN:-}" ]]; then
    secrets+=(demo-log-access-token="$DEMO_LOG_ACCESS_TOKEN")
    env_vars+=(DemoLogging__AccessToken=secretref:demo-log-access-token)
  fi

  az containerapp create \
    --name "$CONTAINER_APP_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --environment "$ENVIRONMENT_NAME" \
    --image "$AGENTBLAZOR_DEMO_IMAGE" \
    --ingress external \
    --target-port 8080 \
    --min-replicas 0 \
    --max-replicas 2 \
    --cpu 0.5 \
    --memory 1Gi \
    --secrets "${secrets[@]}" \
    --env-vars "${env_vars[@]}" \
    --output none
fi

az containerapp show \
  --name "$CONTAINER_APP_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --query "properties.configuration.ingress.fqdn" \
  --output tsv
