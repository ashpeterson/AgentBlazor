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

function buildOverlayFilter({ eyebrow, title, body, duration, crop, titleSize = 48, bodySize = 24, boxY = 728, boxHeight = 176 }) {
  const safeEyebrow = escapeDrawText(eyebrow);
  const safeTitle = escapeDrawText(title);
  const safeBody = escapeDrawText(body);
  const fadeOutStart = Math.max(0.1, duration - 0.35).toFixed(2);
  const titleY = boxY + 66;
  const bodyY = boxY + 124;
  const baseVisual = crop ? `${crop},scale=1600:980` : "scale=1600:980";

  return [
    baseVisual,
    "eq=contrast=1.03:saturation=1.08:brightness=0.015",
    `drawbox=x=44:y=${boxY}:w=1512:h=${boxHeight}:color=0x06111a@0.76:t=fill`,
    `drawbox=x=44:y=${boxY}:w=1512:h=${boxHeight}:color=0xffffff@0.10:t=2`,
    `drawtext=fontfile=${fontBold}:text='${safeEyebrow}':x=76:y=${boxY + 32}:fontsize=22:fontcolor=white@0.72`,
    `drawtext=fontfile=${fontBold}:text='${safeTitle}':x=76:y=${titleY}:fontsize=${titleSize}:fontcolor=white`,
    `drawtext=fontfile=${fontRegular}:text='${safeBody}':x=76:y=${bodyY}:fontsize=${bodySize}:fontcolor=white@0.78`,
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
  const titleSize = config.titleSize ?? 86;
  const bodySize = config.bodySize ?? 30;

  const filter = [
    "drawbox=x=0:y=0:w=1600:h=980:color=0x050b13@1:t=fill",
    "drawbox=x=0:y=0:w=1600:h=6:color=0xff6a3d@0.9:t=fill",
    "drawbox=x=76:y=130:w=718:h=518:color=0x08101a@1:t=fill",
    "drawbox=x=76:y=130:w=718:h=518:color=0xffffff@0.12:t=2",
    "drawbox=x=890:y=108:w=614:h=564:color=0x0c1724@0.98:t=fill",
    "drawbox=x=890:y=108:w=614:h=564:color=0xff6a3d@0.16:t=2",
    "drawbox=x=112:y=168:w=500:h=18:color=0xffffff@0.12:t=fill",
    "drawbox=x=112:y=208:w=420:h=18:color=0xffffff@0.10:t=fill",
    "drawbox=x=112:y=248:w=280:h=18:color=0xffffff@0.08:t=fill",
    "drawbox=x=112:y=348:w=560:h=240:color=0x02060c@1:t=fill",
    "drawbox=x=920:y=144:w=150:h=26:color=0x223447@1:t=fill",
    "drawbox=x=920:y=144:w=150:h=26:color=0xffffff@0.10:t=2",
    `drawtext=fontfile=${fontBold}:text='${safeEyebrow}':x=896:y=86:fontsize=24:fontcolor=white@0.72`,
    `drawtext=fontfile=${fontBold}:text='${safeTitle}':x=896:y=208:fontsize=${titleSize}:line_spacing=8:fontcolor=white`,
    `drawtext=fontfile=${fontRegular}:text='${safeBody}':x=896:y=336:fontsize=${bodySize}:fontcolor=white@0.80`,
    `drawtext=fontfile=${fontBold}:text='dotnet new blazor -n FreshAgentBlazor':x=112:y=164:fontsize=22:fontcolor=white@0.82`,
    `drawtext=fontfile=${fontRegular}:text='dotnet add package AgentBlazor --version 0.1.0-preview.11':x=112:y=208:fontsize=18:fontcolor=white@0.58`,
    `drawtext=fontfile=${fontRegular}:text='Install. Mount one workflow. Watch the UI move.' :x=112:y=578:fontsize=22:fontcolor=white@0.72`,
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
  const supportPath = path.join(sourceDir, "support-inbox-raw.mp4");
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

  if (!fs.existsSync(supportPath)) {
    const existing = path.join(repoRoot, "artifacts/video/support-inbox-demo/support-inbox-agentblazor.mp4");
    assertExists(existing);
    fs.copyFileSync(existing, supportPath);
  }

  if (!fs.existsSync(dashboardPath)) {
    const existing = path.join(repoRoot, "demo/AgentBlazor.Demo/wwwroot/videos/dashboard-live.mp4");
    assertExists(existing);
    fs.copyFileSync(existing, dashboardPath);
  }

  return { cliPath, codePath, supportPath, dashboardPath };
}

async function main() {
  const outDir = path.resolve(process.argv[2] || path.join(repoRoot, "artifacts/video/product-videos"));
  const sourceDir = path.join(outDir, "sources");
  const segmentDir = path.join(outDir, "segments");
  const deliverDir = path.join(outDir, "deliverables");

  ensureDir(sourceDir);
  ensureDir(segmentDir);
  ensureDir(deliverDir);

  const { cliPath, codePath, supportPath, dashboardPath } = ensureSourceClips(sourceDir);

  const introCard = path.join(segmentDir, "intro-card.mp4");
  renderTitleCard(introCard, {
    eyebrow: "AGENTBLAZOR",
    title: "CLI first.\nThen the UI moves.",
    body: "Show the command, the generated shape, and the result.",
    titleSize: 60,
    bodySize: 21,
    duration: 1.6
  });

  const teaserSupportA = path.join(segmentDir, "teaser-support-a.mp4");
  renderClipSegment(supportPath, teaserSupportA, {
    start: 8.9,
    duration: 3.3,
    eyebrow: "REAL APP RESULT",
    title: "Focus the live support queue",
    body: "The chat surface highlights live tickets and explains why they need attention.",
    crop: "crop=1400:840:120:70",
    titleSize: 46,
    bodySize: 22,
    boxY: 744,
    boxHeight: 154
  });

  const teaserSupportB = path.join(segmentDir, "teaser-support-b.mp4");
  renderClipSegment(supportPath, teaserSupportB, {
    start: 18.4,
    duration: 3.6,
    eyebrow: "APPROVAL FLOW",
    title: "Approve the drafted reply",
    body: "Escalation clears the blocker, then the reply draft lands on screen after approval.",
    crop: "crop=1400:840:120:70",
    titleSize: 46,
    bodySize: 22,
    boxY: 744,
    boxHeight: 154
  });

  const dashboardOverview = path.join(segmentDir, "dashboard-overview.mp4");
  renderClipSegment(dashboardPath, dashboardOverview, {
    start: 0.2,
    duration: 1.9,
    eyebrow: "PAID SURFACE",
    title: "Usage stays visible",
    body: "Operators keep the action count, success rate, and timings in view.",
    crop: "crop=1400:760:100:80",
    titleSize: 42,
    bodySize: 21,
    boxY: 744,
    boxHeight: 154
  });

  const dashboardAudit = path.join(segmentDir, "dashboard-audit.mp4");
  renderClipSegment(dashboardPath, dashboardAudit, {
    start: 2.0,
    duration: 1.9,
    eyebrow: "AUDIT + PATTERNS",
    title: "Audit and patterns stay queryable",
    body: "The paid view keeps the operational history close to the workflow.",
    crop: "crop=1400:760:100:80",
    titleSize: 40,
    bodySize: 21,
    boxY: 744,
    boxHeight: 154
  });

  const dashboardHighlight = path.join(deliverDir, "dashboard-live.mp4");
  concatSegments([dashboardOverview, dashboardAudit], dashboardHighlight);

  const heroTeaser = path.join(deliverDir, "agentblazor-hero-teaser.mp4");
  concatSegments([teaserSupportA, teaserSupportB, dashboardOverview], heroTeaser);

  const cliSegment = path.join(segmentDir, "reel-cli.mp4");
  renderClipSegment(cliPath, cliSegment, {
    start: 0.0,
    duration: 10.2,
    eyebrow: "CLI INSTALL",
    title: "Run the full install flow",
    body: "New app, package, tool install, init, scaffold, build, validate.",
    titleSize: 42,
    bodySize: 21,
    boxY: 742,
    boxHeight: 156
  });

  const codeSegment = path.join(segmentDir, "reel-code.mp4");
  renderClipSegment(codePath, codeSegment, {
    start: 0.1,
    duration: 3.8,
    eyebrow: "SMALL CODE SHAPE",
    title: "Keep the app native",
    body: "Wire the runtime, add one capability class, mount one chat surface.",
    crop: "crop=1460:860:70:70",
    titleSize: 44,
    bodySize: 22,
    boxY: 742,
    boxHeight: 156
  });

  const supportSegmentA = path.join(segmentDir, "reel-support-a.mp4");
  renderClipSegment(supportPath, supportSegmentA, {
    start: 7.8,
    duration: 5.2,
    eyebrow: "REAL BLAZOR APP",
    title: "Prompt changes the support queue",
    body: "The route highlights open tickets and turns the queue into a concrete workflow.",
    crop: "crop=1400:840:120:70",
    titleSize: 46,
    bodySize: 22,
    boxY: 742,
    boxHeight: 156
  });

  const supportSegmentB = path.join(segmentDir, "reel-support-b.mp4");
  renderClipSegment(supportPath, supportSegmentB, {
    start: 16.1,
    duration: 6.1,
    eyebrow: "APPROVAL + RESULT",
    title: "Escalate, approve, then send the next step",
    body: "The visible draft card appears after the blocker is cleared and the reply is approved.",
    crop: "crop=1400:840:120:70",
    titleSize: 46,
    bodySize: 22,
    boxY: 742,
    boxHeight: 156
  });

  const capabilityReel = path.join(deliverDir, "agentblazor-capability-reel.mp4");
  concatSegments([introCard, cliSegment, codeSegment, supportSegmentA, supportSegmentB], capabilityReel);

  const supportHighlight = path.join(deliverDir, "support-inbox-agentblazor.mp4");
  concatSegments([teaserSupportA, teaserSupportB], supportHighlight);

  renderPoster(heroTeaser, path.join(deliverDir, "agentblazor-hero-teaser-poster.jpg"), 1.4);
  renderPoster(capabilityReel, path.join(deliverDir, "agentblazor-capability-reel-poster.jpg"), 0.4);
  renderPoster(supportHighlight, path.join(deliverDir, "support-inbox-agentblazor-poster.jpg"), 1.8);
  renderPoster(dashboardHighlight, path.join(deliverDir, "dashboard-live-poster.jpg"), 1.0);

  console.log(heroTeaser);
  console.log(capabilityReel);
  console.log(supportHighlight);
  console.log(dashboardHighlight);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
