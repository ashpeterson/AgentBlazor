#!/usr/bin/env node

const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");

const repoRoot = path.resolve(__dirname, "../..");
const cacheRoot = "/tmp/agentblazor-ms-blazor-samples";
const sampleRelativePath = path.join("10.0", "BlazorWebAppMovies");
const templateRoot = path.join(repoRoot, "scripts", "video", "templates", "ms-movies-demo");

function run(cmd, args, options = {}) {
  const result = spawnSync(cmd, args, {
    stdio: "pipe",
    encoding: "utf8",
    ...options
  });

  if (result.status !== 0) {
    throw new Error(`${cmd} ${args.join(" ")} failed\n${result.stdout}\n${result.stderr}`);
  }

  return result;
}

function ensureOfficialSample() {
  if (!fs.existsSync(cacheRoot)) {
    run("git", ["clone", "--depth", "1", "--filter=blob:none", "--sparse", "https://github.com/dotnet/blazor-samples.git", cacheRoot]);
    run("git", ["sparse-checkout", "set", sampleRelativePath], { cwd: cacheRoot });
    return;
  }

  run("git", ["sparse-checkout", "set", sampleRelativePath], { cwd: cacheRoot });
}

function copyDirectory(sourceDir, targetDir) {
  fs.mkdirSync(targetDir, { recursive: true });
  fs.cpSync(sourceDir, targetDir, { recursive: true });
}

function overlayTemplates(sourceDir, targetDir) {
  for (const entry of fs.readdirSync(sourceDir, { withFileTypes: true })) {
    const sourcePath = path.join(sourceDir, entry.name);
    const targetPath = path.join(targetDir, entry.name.replace(/\.template$/, ""));

    if (entry.isDirectory()) {
      fs.mkdirSync(targetPath, { recursive: true });
      overlayTemplates(sourcePath, targetPath);
      continue;
    }

    const content = fs.readFileSync(sourcePath, "utf8")
      .replaceAll("__AGENTBLAZOR_REPO_ROOT__", repoRoot.replaceAll("\\", "/"));
    fs.mkdirSync(path.dirname(targetPath), { recursive: true });
    fs.writeFileSync(targetPath, content);
  }
}

function main() {
  const targetRoot = path.resolve(process.argv[2] || path.join(repoRoot, "artifacts", "video", "ms-movies-demo", "workspace"));
  const sampleTargetDir = path.join(targetRoot, "BlazorWebAppMovies");

  ensureOfficialSample();
  fs.rmSync(targetRoot, { recursive: true, force: true });
  fs.mkdirSync(targetRoot, { recursive: true });
  copyDirectory(path.join(cacheRoot, sampleRelativePath), sampleTargetDir);
  fs.rmSync(path.join(sampleTargetDir, "Migrations"), { recursive: true, force: true });
  overlayTemplates(templateRoot, sampleTargetDir);

  console.log(sampleTargetDir);
}

try {
  main();
} catch (error) {
  console.error(error);
  process.exit(1);
}
