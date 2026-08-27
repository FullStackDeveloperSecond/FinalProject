import { defineConfig, devices } from '@playwright/test'

const isCi = Boolean(process.env.CI)

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: isCi,
  retries: isCi ? 1 : 0,
  workers: isCi ? 1 : undefined,
  outputDir: 'test-results',
  reporter: isCi
    ? [
        ['line'],
        ['html', { outputFolder: 'playwright-report', open: 'never' }],
      ]
    : 'list',
  use: {
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    {
      name: 'customer-chromium',
      testMatch: /customer\.spec\.ts/,
      use: {
        ...devices['Desktop Chrome'],
        baseURL: 'http://127.0.0.1:5173',
      },
    },
    {
      name: 'admin-chromium',
      testMatch: /admin\.spec\.ts/,
      use: {
        ...devices['Desktop Chrome'],
        baseURL: 'http://127.0.0.1:5174/admin/',
      },
    },
  ],
  webServer: [
    {
      command: 'dotnet run --project ../../src/backend/DoSelect.Api/DoSelect.Api.csproj --no-launch-profile',
      url: 'http://127.0.0.1:5126/health/live',
      reuseExistingServer: !isCi,
      timeout: 120_000,
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: 'E2E',
        ASPNETCORE_URLS: 'http://127.0.0.1:5126',
        Features__BackgroundJobsEnabled: 'false',
        Features__EmailEnabled: 'false',
      },
    },
    {
      command: 'npm run dev -- --host 127.0.0.1 --port 5173 --strictPort',
      url: 'http://127.0.0.1:5173',
      reuseExistingServer: !isCi,
      timeout: 60_000,
    },
    {
      command: 'npm --prefix ../admin-web run dev -- --host 127.0.0.1 --port 5174 --strictPort',
      url: 'http://127.0.0.1:5174/admin/',
      reuseExistingServer: !isCi,
      timeout: 60_000,
    },
  ],
})
