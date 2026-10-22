import { defineConfig, devices } from '@playwright/test';

const apiPort = process.env.E2E_API_PORT ?? '5080';
const webPort = process.env.E2E_WEB_PORT ?? '3000';
const apiUrl = `http://127.0.0.1:${apiPort}`;
const webUrl = `http://localhost:${webPort}`;
// An already-running dev server was not started with these overrides.
// Reusing it would leave the web app on port 5080 or the API with the old CORS origin.
const reuseDevServers = !process.env.CI
  && process.env.E2E_API_PORT === undefined
  && process.env.E2E_WEB_PORT === undefined;

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
      env: {
        Cors__AllowedOrigins: webUrl,
        ConnectionStrings__GrabBike: process.env.ConnectionStrings__GrabBike
          ?? 'Host=localhost;Port=5432;Database=happyride;Username=happyride;Password=happyride',
        'Dispatch__Bus': 'InMemory',
        'Maps__Provider': process.env.E2E_MAPS_PROVIDER ?? 'Fake',
        'Maps__VietMap__ApiKey': process.env.E2E_VIETMAP_API_KEY ?? '',
        'Maps__VietMap__TileKey': process.env.E2E_VIETMAP_TILE_KEY ?? '',
      },
      name: 'api',
      reuseExistingServer: reuseDevServers,
      timeout: 300000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
    {
      command: `node scripts/copy-vietmap-gl.mjs && npm exec -- next dev --port ${webPort}`,
      cwd: '../web',
      url: webUrl,
      env: { API_BASE_URL: apiUrl, NEXT_PUBLIC_API_BASE_URL: apiUrl },
      name: 'web',
      reuseExistingServer: reuseDevServers,
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
