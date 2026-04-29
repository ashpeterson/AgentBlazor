#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
OUT_DIR="${1:-$REPO_ROOT/artifacts/video/cli-install}"
CAST_FILE="$OUT_DIR/cli-install.cast"
TEMP_ROOT="$(mktemp -d /tmp/agentblazor-cli-video-XXXXXX)"
WORK_DIR="$TEMP_ROOT/workspace"
SESSION_SCRIPT="$TEMP_ROOT/session.sh"
SESSION_OUTPUT_DIR="$OUT_DIR/generated-project"

cleanup() {
    rm -rf "$TEMP_ROOT"
}

trap cleanup EXIT

mkdir -p "$OUT_DIR" "$WORK_DIR" "$SESSION_OUTPUT_DIR"

GH_TOKEN="$(gh auth token)"
if [[ -z "$GH_TOKEN" ]]; then
    echo "GitHub auth token was not available via gh auth token." >&2
    exit 1
fi

cat > "$WORK_DIR/NuGet.Config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="github-agentblazor" value="https://nuget.pkg.github.com/ashpeterson/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github-agentblazor>
      <add key="Username" value="gh" />
      <add key="ClearTextPassword" value="$GH_TOKEN" />
    </github-agentblazor>
  </packageSourceCredentials>
</configuration>
EOF

cat > "$SESSION_SCRIPT" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail

WORK_DIR="$1"
SESSION_OUTPUT_DIR="$2"

cd "$WORK_DIR"

run_step() {
    local display="$1"
    shift
    printf '\033[1;32m$ %s\033[0m\n' "$display"
    "$@"
    printf '\n'
    sleep 0.8
}

run_step "dotnet new blazor -n FreshAgentBlazor" \
    dotnet new blazor -n FreshAgentBlazor

cd FreshAgentBlazor

run_step "dotnet add package AgentBlazor --version 0.1.0-preview.10 --source github-agentblazor" \
    dotnet add package AgentBlazor --version 0.1.0-preview.10 --source github-agentblazor

run_step "dotnet tool install AgentBlazor.Cli --tool-path ./.tools --version 0.1.0-preview.10 --add-source https://nuget.pkg.github.com/ashpeterson/index.json" \
    dotnet tool install AgentBlazor.Cli --tool-path ./.tools --version 0.1.0-preview.10 --add-source https://nuget.pkg.github.com/ashpeterson/index.json

run_step "./.tools/agentblazor init ./FreshAgentBlazor.csproj --host FreshAgentBlazor" \
    ./.tools/agentblazor init ./FreshAgentBlazor.csproj --host FreshAgentBlazor

run_step "./.tools/agentblazor scaffold ./FreshAgentBlazor.csproj --host FreshAgentBlazor --provider openai --diff" \
    ./.tools/agentblazor scaffold ./FreshAgentBlazor.csproj --host FreshAgentBlazor --provider openai --diff

run_step "./.tools/agentblazor scaffold ./FreshAgentBlazor.csproj --host FreshAgentBlazor --provider openai --approve" \
    ./.tools/agentblazor scaffold ./FreshAgentBlazor.csproj --host FreshAgentBlazor --provider openai --approve

run_step "dotnet restore ./FreshAgentBlazor.csproj --force-evaluate" \
    dotnet restore ./FreshAgentBlazor.csproj --force-evaluate

run_step "dotnet build ./FreshAgentBlazor.csproj --no-restore -nologo" \
    dotnet build ./FreshAgentBlazor.csproj --no-restore -nologo

run_step "./.tools/agentblazor doctor ./FreshAgentBlazor.csproj --host FreshAgentBlazor" \
    ./.tools/agentblazor doctor ./FreshAgentBlazor.csproj --host FreshAgentBlazor

run_step "./.tools/agentblazor validate ./FreshAgentBlazor.csproj --host FreshAgentBlazor" \
    ./.tools/agentblazor validate ./FreshAgentBlazor.csproj --host FreshAgentBlazor

cp -R "$WORK_DIR/FreshAgentBlazor" "$SESSION_OUTPUT_DIR/FreshAgentBlazor"
EOF

chmod +x "$SESSION_SCRIPT"

TERM=xterm-256color COLUMNS=100 LINES=30 \
    asciinema rec --overwrite --quiet --cols 100 --rows 30 \
    --command "bash '$SESSION_SCRIPT' '$WORK_DIR' '$SESSION_OUTPUT_DIR'" \
    "$CAST_FILE"

printf 'Cast written to %s\n' "$CAST_FILE"
