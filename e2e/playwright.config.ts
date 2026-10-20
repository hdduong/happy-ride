import { defineConfig, devices } from '@playwright/test';

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
    baseURL: 'http://localhost:3000',
    actionTimeout: 20000,
    navigationTimeout: 60000,
    trace: 'retain-on-failure',
  },
  webServer: [
    {
      command: 'dotnet run --project ../src/GrabBike.Api --urls http://127.0.0.1:5080',
      url: 'http://127.0.0.1:5080/api/map-config',
      name: 'api',
      reuseExistingServer: true,
      timeout: 180000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
    {
      command: 'npm --prefix ../web run dev',
      url: 'http://127.0.0.1:3000',
      name: 'web',
      reuseExistingServer: true,
      timeout: 180000,
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
