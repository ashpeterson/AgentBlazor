const { defineConfig } = require("@playwright/test");

module.exports = defineConfig({
  testDir: "./specs",
  // The demo server and prompt-backed orchestration flows are resource-sensitive; keep
  // the default run serial and allow overrides when higher parallelism is safe.
  workers: process.env.PLAYWRIGHT_WORKERS ? Number(process.env.PLAYWRIGHT_WORKERS) : 1,
  timeout: 180000,
  expect: {
    timeout: 30000
  },
  outputDir: "./test-results",
  reporter: [["list"], ["html", { open: "never", outputFolder: "./playwright-report" }]],
  use: {
    baseURL: "http://127.0.0.1:5188",
    headless: true,
    ignoreHTTPSErrors: true,
    screenshot: "only-on-failure",
    trace: "retain-on-failure",
    video: "retain-on-failure"
  },
  webServer: {
    command: "dotnet run --project ../../demo/AgentBlazor.Demo/AgentBlazor.Demo.csproj --urls http://127.0.0.1:5188",
    url: "http://127.0.0.1:5188/demo/workflows/response-orchestration?reset=true",
    timeout: 180000,
    reuseExistingServer: process.env.PLAYWRIGHT_REUSE_SERVER === "1"
  }
});
