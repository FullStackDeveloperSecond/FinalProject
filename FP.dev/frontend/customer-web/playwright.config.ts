import { defineConfig, devices } from '@playwright/test'
import os from 'node:os'
import path from 'node:path'

const isCi = Boolean(process.env.CI)
const e2eDataRoot = process.env.E2E_STORAGE_DATA_ROOT ?? path.join(os.tmpdir(), 'doselect-e2e-data')
const e2eConnectionString = process.env.ConnectionStrings__DefaultConnection
const reuseExistingServer = !isCi && process.env.E2E_REUSE_EXISTING_SERVER !== 'false'
const apiEnvironment = process.env.E2E_ASPNETCORE_ENVIRONMENT ?? 'E2E'
const backgroundJobsEnabled = process.env.E2E_BACKGROUND_JOBS_ENABLED ?? 'false'
const simulationEndpointsEnabled = process.env.E2E_SIMULATION_ENDPOINTS_ENABLED ?? 'false'

if (
  !e2eConnectionString ||
  !/(?:Database|Initial Catalog)\s*=\s*DoSelectE2E(?:_[0-9a-f]{32})?(?:;|$)/i.test(e2eConnectionString)
) {
  throw new Error(
    'Playwright requires an isolated DoSelectE2E database. Run scripts/test-customer-e2e.ps1 instead of targeting the shared DoSelectDb.',
  )
}

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
      reuseExistingServer,
      timeout: 120_000,
      env: {
        ...process.env,
        ConnectionStrings__DefaultConnection: e2eConnectionString,
        ASPNETCORE_ENVIRONMENT: apiEnvironment,
        ASPNETCORE_URLS: 'http://127.0.0.1:5126',
        Features__BackgroundJobsEnabled: backgroundJobsEnabled,
        Features__EmailEnabled: 'false',
        Demo__SimulationEndpointsEnabled: simulationEndpointsEnabled,
        GuestOrderAccess__Pepper: 'e2e-guest-order-access-pepper-32-bytes',
        Idempotency__ActorScopePepper: 'e2e-idempotency-actor-scope-pepper-32-bytes',
        Security__CouponGuestUsageHmacKeyV1: 'e2e-coupon-guest-usage-hmac-key-v1-32-bytes',
        DataProtection__KeyRingPath: path.join(e2eDataRoot, 'data-protection-keys'),
        Storage__DataRoot: e2eDataRoot,
      },
    },
    {
      command: 'npm run dev -- --host 127.0.0.1 --port 5173 --strictPort',
      url: 'http://127.0.0.1:5173',
      reuseExistingServer,
      timeout: 60_000,
      env: {
        ...process.env,
        VITE_API_BASE_URL: 'http://127.0.0.1:5173',
      },
    },
    {
      command: 'npm --prefix ../admin-web run dev -- --host 127.0.0.1 --port 5174 --strictPort',
      url: 'http://127.0.0.1:5174/admin/',
      reuseExistingServer,
      timeout: 60_000,
      env: {
        ...process.env,
        VITE_API_BASE_URL: 'http://127.0.0.1:5174',
      },
    },
  ],
})
