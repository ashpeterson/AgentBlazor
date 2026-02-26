const { defineConfig } = require("@playwright/test");

module.exports = defineConfig({
  testDir: "./specs",
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
    url: "http://127.0.0.1:5188/demo/generative-ui",
    timeout: 180000,
    reuseExistingServer: false
  }
});
