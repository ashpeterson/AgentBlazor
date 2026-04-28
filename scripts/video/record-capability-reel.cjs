#!/usr/bin/env node

const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");

const repoRoot = path.resolve(__dirname, "../..");

function run(cmd, args, options = {}) {
  const result = spawnSync(cmd, args, {
    stdio: "pipe",
    encoding: "utf8",
    ...options
  });

  if (result.status !== 0) {
    throw new Error(`${cmd} ${args.join(" ")} failed\n${result.stdout}\n${result.stderr}`);
  }

  return result.stdout.trim();
}

function ensureDir(dirPath) {
  fs.mkdirSync(dirPath, { recursive: true });
}

function normalizeClip(inputPath, outputPath) {
  run("ffmpeg", [
    "-y",
    "-i",
    inputPath,
    "-vf",
    "scale=1600:980:force_original_aspect_ratio=decrease,pad=1600:980:(ow-iw)/2:(oh-ih)/2:black",
    "-r",
    "25",
    "-c:v",
    "libx264",
    "-pix_fmt",
    "yuv420p",
    "-movflags",
    "+faststart",
    outputPath
  ]);
}

async function main() {
  const outDir = path.resolve(process.argv[2] || path.join(repoRoot, "artifacts/video/capability-reel"));
  const cliDir = path.join(outDir, "cli-install");
  const codeDir = path.join(outDir, "code-tour");
  const moviesDir = path.join(outDir, "ms-movies-demo");
  const normalizedDir = path.join(outDir, "normalized");
  const repoWorkflowClip = path.join(repoRoot, "artifacts/video/ms-movies-demo-e2e/ms-movies-agentblazor.mp4");
  const concatListPath = path.join(outDir, "concat.txt");
  const finalPath = path.join(outDir, "agentblazor-capability-reel.mp4");

  ensureDir(outDir);
  ensureDir(normalizedDir);

  run("bash", [path.join(repoRoot, "scripts/video/record-cli-install-demo.sh"), cliDir], { cwd: repoRoot });
  run("node", [path.join(repoRoot, "scripts/video/render-terminal-video.cjs"), path.join(cliDir, "cli-install.cast"), cliDir], { cwd: repoRoot });
  run("node", [path.join(repoRoot, "scripts/video/record-code-tour.cjs"), codeDir], { cwd: repoRoot });

  const workflowClipPath = fs.existsSync(repoWorkflowClip)
    ? repoWorkflowClip
    : (() => {
        run("node", [path.join(repoRoot, "scripts/video/record-ms-movies-demo.cjs"), moviesDir], {
          cwd: repoRoot,
          env: process.env
        });
        return path.join(moviesDir, "ms-movies-agentblazor.mp4");
      })();

  const parts = [
    path.join(cliDir, "cli-install.mp4"),
    path.join(codeDir, "code-tour.mp4"),
    workflowClipPath
  ];

  const normalizedParts = parts.map((part, index) => {
    const normalizedPath = path.join(normalizedDir, `part-${index + 1}.mp4`);
    normalizeClip(part, normalizedPath);
    return normalizedPath;
  });

  fs.writeFileSync(
    concatListPath,
    normalizedParts.map((part) => `file '${part.replace(/'/g, "'\\''")}'`).join("\n") + "\n"
  );

  run("ffmpeg", [
    "-y",
    "-f",
    "concat",
    "-safe",
    "0",
    "-i",
    concatListPath,
    "-c",
    "copy",
    finalPath
  ]);

  console.log(finalPath);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
