#!/usr/bin/env node

const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");

const repoRoot = path.resolve(__dirname, "../..");
const fontBold = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf";
const fontRegular = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";

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

function assertExists(filePath) {
  if (!fs.existsSync(filePath)) {
    throw new Error(`Missing required source clip: ${filePath}`);
  }
}

function escapeDrawText(value) {
  return value
    .replace(/\\/g, "\\\\")
    .replace(/:/g, "\\:")
    .replace(/'/g, "\\'")
    .replace(/,/g, "\\,")
    .replace(/\[/g, "\\[")
    .replace(/\]/g, "\\]")
    .replace(/%/g, "\\%");
}

function buildOverlayFilter({ eyebrow, title, body, duration }) {
  const safeEyebrow = escapeDrawText(eyebrow);
  const safeTitle = escapeDrawText(title);
  const safeBody = escapeDrawText(body);
  const fadeOutStart = Math.max(0.1, duration - 0.35).toFixed(2);

  return [
    "scale=1600:980",
    "eq=contrast=1.03:saturation=1.08:brightness=0.015",
    "drawbox=x=44:y=728:w=1512:h=176:color=0x06111a@0.74:t=fill",
    "drawbox=x=44:y=728:w=1512:h=176:color=0xffffff@0.10:t=2",
    `drawtext=fontfile=${fontBold}:text='${safeEyebrow}':x=76:y=760:fontsize=22:fontcolor=white@0.72`,
    `drawtext=fontfile=${fontBold}:text='${safeTitle}':x=76:y=794:fontsize=48:fontcolor=white`,
    `drawtext=fontfile=${fontRegular}:text='${safeBody}':x=76:y=852:fontsize=24:fontcolor=white@0.78`,
    `fade=t=in:st=0:d=0.25,fade=t=out:st=${fadeOutStart}:d=0.25`
  ].join(",");
}

function renderClipSegment(inputPath, outputPath, config) {
  const filter = buildOverlayFilter(config);
  run("ffmpeg", [
    "-y",
    "-ss",
    String(config.start),
    "-t",
    String(config.duration),
    "-i",
    inputPath,
    "-an",
    "-vf",
    filter,
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

function renderTitleCard(outputPath, config) {
  const safeEyebrow = escapeDrawText(config.eyebrow);
  const safeTitle = escapeDrawText(config.title);
  const safeBody = escapeDrawText(config.body);
  const duration = String(config.duration);

  const filter = [
    "drawbox=x=0:y=0:w=1600:h=980:color=0x06111a@1:t=fill",
    "drawbox=x=1110:y=0:w=490:h=980:color=0x1a1823@0.95:t=fill",
    "drawbox=x=0:y=0:w=960:h=980:color=0x081522@0.94:t=fill",
    "drawbox=x=64:y=188:w=720:h=392:color=0xffffff@0.06:t=fill",
    "drawbox=x=64:y=188:w=720:h=392:color=0xffffff@0.90:t=2",
    `drawtext=fontfile=${fontBold}:text='${safeEyebrow}':x=128:y=136:fontsize=24:fontcolor=white@0.72`,
    `drawtext=fontfile=${fontBold}:text='${safeTitle}':x=128:y=210:fontsize=92:line_spacing=8:fontcolor=white`,
    `drawtext=fontfile=${fontRegular}:text='${safeBody}':x=128:y=638:fontsize=30:fontcolor=white@0.82`,
    `fade=t=in:st=0:d=0.2,fade=t=out:st=${Math.max(0.1, config.duration - 0.25).toFixed(2)}:d=0.2`
  ].join(",");

  run("ffmpeg", [
    "-y",
    "-f",
    "lavfi",
    "-i",
    `color=c=#06111a:s=1600x980:d=${duration}`,
    "-an",
    "-vf",
    filter,
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

function concatSegments(parts, outputPath) {
  const concatPath = path.join(path.dirname(outputPath), `${path.basename(outputPath, ".mp4")}.txt`);
  fs.writeFileSync(
    concatPath,
    parts.map((part) => `file '${part.replace(/'/g, "'\\''")}'`).join("\n") + "\n"
  );

  run("ffmpeg", [
    "-y",
    "-f",
    "concat",
    "-safe",
    "0",
    "-i",
    concatPath,
    "-c",
    "copy",
    outputPath
  ]);
}

function renderPoster(inputPath, outputPath, second) {
  run("ffmpeg", [
    "-y",
    "-ss",
    String(second),
    "-i",
    inputPath,
    "-frames:v",
    "1",
    "-update",
    "1",
    outputPath
  ]);
}

function ensureSourceClips(sourceDir) {
  const cliPath = path.join(sourceDir, "cli-install.mp4");
  const codePath = path.join(sourceDir, "code-tour.mp4");
  const moviesPath = path.join(sourceDir, "ms-movies-raw.mp4");
  const dashboardPath = path.join(sourceDir, "dashboard-live.mp4");

  if (!fs.existsSync(cliPath)) {
    const existing = path.join(repoRoot, "artifacts/video/capability-reel-e2e/cli-install/cli-install.mp4");
    assertExists(existing);
    fs.copyFileSync(existing, cliPath);
  }

  if (!fs.existsSync(codePath)) {
    const existing = path.join(repoRoot, "artifacts/video/capability-reel-e2e/code-tour/code-tour.mp4");
    assertExists(existing);
    fs.copyFileSync(existing, codePath);
  }

  if (!fs.existsSync(moviesPath)) {
    const existing = path.join(repoRoot, "artifacts/video/ms-movies-demo-e2e/ms-movies-agentblazor.mp4");
    assertExists(existing);
    fs.copyFileSync(existing, moviesPath);
  }

  if (!fs.existsSync(dashboardPath)) {
    const existing = path.join(repoRoot, "demo/AgentBlazor.Demo/wwwroot/videos/dashboard-live.mp4");
    assertExists(existing);
    fs.copyFileSync(existing, dashboardPath);
  }

  return { cliPath, codePath, moviesPath, dashboardPath };
}

async function main() {
  const outDir = path.resolve(process.argv[2] || path.join(repoRoot, "artifacts/video/product-videos"));
  const sourceDir = path.join(outDir, "sources");
  const segmentDir = path.join(outDir, "segments");
  const deliverDir = path.join(outDir, "deliverables");

  ensureDir(sourceDir);
  ensureDir(segmentDir);
  ensureDir(deliverDir);

  const { cliPath, codePath, moviesPath, dashboardPath } = ensureSourceClips(sourceDir);

  const introCard = path.join(segmentDir, "intro-card.mp4");
  renderTitleCard(introCard, {
    eyebrow: "AGENTBLAZOR",
    title: "Prompt. Approve.\nWatch the UI move.",
    body: "Real Blazor app first. Then the minimal code and CLI that made it happen.",
    duration: 1.8
  });

  const teaserMovieA = path.join(segmentDir, "teaser-movie-a.mp4");
  renderClipSegment(moviesPath, teaserMovieA, {
    start: 10.8,
    duration: 3.8,
    eyebrow: "REAL APP RESULT",
    title: "Filter and focus a real page",
    body: "The chat surface drives the Microsoft movie sample, not a synthetic mock.",
  });

  const teaserMovieB = path.join(segmentDir, "teaser-movie-b.mp4");
  renderClipSegment(moviesPath, teaserMovieB, {
    start: 17.2,
    duration: 3.9,
    eyebrow: "APPROVAL FLOW",
    title: "Approve and keep moving",
    body: "The draft card lands on screen after the approval boundary is cleared."
  });

  const teaserDashboard = path.join(segmentDir, "teaser-dashboard.mp4");
  renderClipSegment(dashboardPath, teaserDashboard, {
    start: 0.4,
    duration: 3.2,
    eyebrow: "PAID SURFACE",
    title: "Usage and audit stay visible",
    body: "Operators can inspect actions, audit, and patterns after real activity."
  });

  const heroTeaser = path.join(deliverDir, "agentblazor-hero-teaser.mp4");
  concatSegments([teaserMovieA, teaserMovieB, teaserDashboard], heroTeaser);

  const cliSegment = path.join(segmentDir, "reel-cli.mp4");
  renderClipSegment(cliPath, cliSegment, {
    start: 0.6,
    duration: 4.8,
    eyebrow: "CLI INSTALL",
    title: "Install it fast",
    body: "Create the app, add the package, run the setup path, move on."
  });

  const codeSegment = path.join(segmentDir, "reel-code.mp4");
  renderClipSegment(codePath, codeSegment, {
    start: 0.3,
    duration: 6.2,
    eyebrow: "SMALL CODE SHAPE",
    title: "Keep the app native",
    body: "Wire the runtime, add one capability class, mount one chat surface."
  });

  const movieSegmentA = path.join(segmentDir, "reel-movie-a.mp4");
  renderClipSegment(moviesPath, movieSegmentA, {
    start: 10.8,
    duration: 4.2,
    eyebrow: "REAL BLAZOR APP",
    title: "Prompt changes the page",
    body: "The catalog filters and the workflow focuses the movie in view."
  });

  const movieSegmentB = path.join(segmentDir, "reel-movie-b.mp4");
  renderClipSegment(moviesPath, movieSegmentB, {
    start: 16.6,
    duration: 4.8,
    eyebrow: "APPROVAL + RESULT",
    title: "Approve, then ship the next step",
    body: "The draft card appears in the workflow after operator approval."
  });

  const dashboardSegment = path.join(segmentDir, "reel-dashboard.mp4");
  renderClipSegment(dashboardPath, dashboardSegment, {
    start: 0.4,
    duration: 3.8,
    eyebrow: "PRO DASHBOARD",
    title: "See what happened",
    body: "Usage, audit, and patterns stay visible for the paid tier."
  });

  const capabilityReel = path.join(deliverDir, "agentblazor-capability-reel.mp4");
  concatSegments([introCard, movieSegmentA, cliSegment, codeSegment, movieSegmentB, dashboardSegment], capabilityReel);

  const moviesHighlight = path.join(deliverDir, "ms-movies-agentblazor.mp4");
  concatSegments([teaserMovieA, teaserMovieB], moviesHighlight);

  renderPoster(heroTeaser, path.join(deliverDir, "agentblazor-hero-teaser-poster.jpg"), 2.2);
  renderPoster(capabilityReel, path.join(deliverDir, "agentblazor-capability-reel-poster.jpg"), 8.0);
  renderPoster(moviesHighlight, path.join(deliverDir, "ms-movies-agentblazor-poster.jpg"), 3.8);

  console.log(heroTeaser);
  console.log(capabilityReel);
  console.log(moviesHighlight);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
