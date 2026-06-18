#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
scratch_root="${AGENTBLAZOR_V2_SMOKE_ROOT:-/tmp/agentblazor-cli-v2-workflow-smoke}"
pack_dir="$scratch_root/packages"
tool_dir="$scratch_root/tool"
nuget_dir="$scratch_root/nuget"
preview_app="$scratch_root/preview-app"
approve_app="$scratch_root/approve-app"
nuget_config="$nuget_dir/NuGet.Config"

log() {
  printf '==> %s\n' "$1"
}

fail() {
  printf 'ERROR: %s\n' "$1" >&2
  exit 1
}

assert_file() {
  local path="$1"
  [[ -f "$path" ]] || fail "Expected file '$path' to exist."
}

assert_contains() {
  local path="$1"
  local pattern="$2"
  rg -q --fixed-strings "$pattern" "$path" || fail "Expected '$path' to contain '$pattern'."
}

assert_not_contains() {
  local path="$1"
  local pattern="$2"
  if rg -q --fixed-strings "$pattern" "$path"; then
    fail "Expected '$path' not to contain '$pattern'."
  fi
}

resolve_tool() {
  if [[ -x "$tool_dir/agentblazor" ]]; then
    printf '%s\n' "$tool_dir/agentblazor"
    return
  fi

  if [[ -x "$tool_dir/agentblazor.exe" ]]; then
    printf '%s\n' "$tool_dir/agentblazor.exe"
    return
  fi

  fail "AgentBlazor CLI executable was not installed under '$tool_dir'."
}

log "Resetting scratch workspace"
rm -rf "$scratch_root"
mkdir -p "$pack_dir" "$tool_dir" "$nuget_dir"

log "Packing AgentBlazor.Cli"
pack_args=(
  "pack"
  "$repo_root/src/AgentBlazor.Cli/AgentBlazor.Cli.csproj"
  "--no-restore"
  "-o"
  "$pack_dir"
)
if [[ -n "${AGENTBLAZOR_CLI_PACKAGE_VERSION:-}" ]]; then
  pack_args+=(
    "/p:Version=$AGENTBLAZOR_CLI_PACKAGE_VERSION"
    "/p:PackageVersion=$AGENTBLAZOR_CLI_PACKAGE_VERSION"
  )
fi
dotnet "${pack_args[@]}"

package_path="$(find "$pack_dir" -maxdepth 1 -name 'AgentBlazor.Cli.*.nupkg' ! -name '*.symbols.nupkg' | sort | tail -n 1)"
[[ -n "$package_path" ]] || fail "AgentBlazor.Cli package was not produced."
package_name="$(basename "$package_path")"
package_version="${package_name#AgentBlazor.Cli.}"
package_version="${package_version%.nupkg}"
expected_tool_version="${AGENTBLAZOR_EXPECTED_TOOL_VERSION:-$package_version}"

log "Writing isolated NuGet configuration"
cp "$pack_dir"/*.nupkg "$nuget_dir"/
cat > "$nuget_config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-agentblazor-cli" value="$nuget_dir" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local-agentblazor-cli">
      <package pattern="AgentBlazor.Cli" />
      <package pattern="agentblazor.cli" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF

log "Installing packaged AgentBlazor.Cli tool"
dotnet tool install AgentBlazor.Cli \
  --version "$package_version" \
  --tool-path "$tool_dir" \
  --configfile "$nuget_config"

cli_exe="$(resolve_tool)"
tool_version="$("$cli_exe" --version | tr -d '\r\n')"
[[ "$tool_version" == "$expected_tool_version" ]] ||
  fail "Installed tool reported version '$tool_version', expected '$expected_tool_version'."

log "Running packaged workflow preview"
cp -R "$repo_root/tests/cli-targets/realistic-blazor-app" "$preview_app"
"$cli_exe" scaffold workflows "$preview_app/RealisticBlazorApp.csproj" \
  --description "Inventory operations app" \
  --agent-goals "prepare restock plans" \
  --diff \
  --non-interactive

[[ ! -e "$preview_app/.agentblazor/SOUL.md" ]] ||
  fail "Preview-only workflow scaffold should not write SOUL.md."

log "Running packaged workflow approval with audit"
cp -R "$repo_root/tests/cli-targets/realistic-blazor-app" "$approve_app"
"$cli_exe" scaffold workflows "$approve_app/RealisticBlazorApp.csproj" \
  --workflow same-service-lifecycle-inventory-pipeline \
  --description "Inventory operations app" \
  --agent-goals "prepare restock plans" \
  --reviewed-by "Package Smoke" \
  --approve \
  --non-interactive

agent_dir="$approve_app/.agentblazor"
assert_file "$agent_dir/workflow-onboarding.json"
assert_file "$agent_dir/workflow-onboarding.md"
assert_file "$agent_dir/workflow-onboarding.html"
assert_file "$agent_dir/SOUL.md"
assert_file "$agent_dir/skills/index.json"
assert_file "$agent_dir/skills/.metadata.json"
assert_file "$agent_dir/skills/inventory-pipeline/SKILL.md"
assert_file "$agent_dir/skills/inventory-pipeline/references/evidence.md"

audit_path="$(find "$agent_dir/audit" -maxdepth 1 -name 'workflow-onboarding-*.json' | sort | tail -n 1)"
[[ -n "$audit_path" ]] || fail "Workflow onboarding audit record was not generated."

assert_contains "$agent_dir/workflow-onboarding.json" '"reviewedBy": "Package Smoke"'
assert_contains "$agent_dir/workflow-onboarding.json" '"status": "approved"'
assert_contains "$agent_dir/workflow-onboarding.html" 'Reviewed by: Package Smoke'
assert_contains "$agent_dir/SOUL.md" 'Inventory Pipeline'
assert_contains "$agent_dir/skills/inventory-pipeline/SKILL.md" 'requiredApprovals'
assert_contains "$audit_path" '"reviewedBy": "Package Smoke"'
assert_contains "$audit_path" 'same-service-lifecycle-inventory-pipeline'
assert_contains "$audit_path" 'propose_patch'
assert_contains "$audit_path" 'apply_approved_patch'
assert_not_contains "$audit_path" "$approve_app"

log "Verifying non-interactive approval requires reviewer identity"
reviewer_gate_app="$scratch_root/reviewer-gate-app"
cp -R "$repo_root/tests/cli-targets/realistic-blazor-app" "$reviewer_gate_app"
set +e
reviewer_gate_output="$("$cli_exe" scaffold workflows "$reviewer_gate_app/RealisticBlazorApp.csproj" \
  --workflow same-service-lifecycle-inventory-pipeline \
  --approve \
  --non-interactive 2>&1)"
reviewer_gate_exit=$?
set -e
[[ "$reviewer_gate_exit" -ne 0 ]] || fail "Workflow approval without --reviewed-by should fail."
printf '%s\n' "$reviewer_gate_output" | rg -q --fixed-strings 'requires --reviewed-by' ||
  fail "Reviewer gate output did not mention --reviewed-by."

log "CLI V2 workflow package smoke passed for AgentBlazor.Cli $package_version"
