#!/usr/bin/env bash
# Run the demo app; [AgentFlow] logs will appear when you run a prompt.
# Open http://localhost:5249 and use the home prompt bar or chat widget.
set -e
cd "$(dirname "$0")/../.."
echo "Building and starting demo (AgentFlow logging enabled)..."
echo "App URL: http://localhost:5249"
echo "Run a prompt (e.g. 'show me all suppliers that are high risk'), then check for [AgentFlow] lines below."
echo ""
dotnet run --project demo/AgentBlazor.Demo/AgentBlazor.Demo.csproj
