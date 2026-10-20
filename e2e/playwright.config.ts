import { defineConfig, devices } from '@playwright/test';

const apiPort = process.env.E2E_API_PORT ?? '5080';
const webPort = process.env.E2E_WEB_PORT ?? '3000';
const apiUrl = `http://127.0.0.1:${apiPort}`;
const webUrl = `http://localhost:${webPort}`;

export default defineConfig({
  testDir: './tests',
  timeout: 180000,
  expect: {
    timeout: 20000,
  },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  use: {
    baseURL: webUrl,
    actionTimeout: 20000,
    navigationTimeout: 60000,
    trace: 'retain-on-failure',
  },
  webServer: [
    {
      command: `dotnet run --project ../src/GrabBike.Api --no-launch-profile --urls ${apiUrl}`,
      url: `${apiUrl}/api/map-config`,
      env: { Cors__AllowedOrigins: webUrl },
      name: 'api',
      reuseExistingServer: !process.env.CI,
      timeout: 300000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
    {
      command: `npm --prefix ../web run dev -- --port ${webPort}`,
      url: webUrl,
      env: { NEXT_PUBLIC_API_BASE_URL: apiUrl },
      name: 'web',
      reuseExistingServer: !process.env.CI,
      timeout: 300000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
  ],
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
