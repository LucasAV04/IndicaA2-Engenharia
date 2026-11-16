import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: './e2e',
  workers: 1,
  retries: 0,
  reporter: [['./e2e/safe-reporter.ts']],
  outputDir: process.env.INDICAA2_UI_OUTPUT_DIR ?? '../../.homologacao-smoke/playwright',
  timeout: 65_000,
  globalTimeout: 120_000,
  use: {
    browserName: 'chromium',
    actionTimeout: 8000,
    navigationTimeout: 12000,
    baseURL: process.env.INDICAA2_UI_ORIGIN ?? 'https://localhost:18443',
    ignoreHTTPSErrors: true,
    screenshot: 'off',
    video: 'off',
    trace: 'off',
    serviceWorkers: 'block',
  },
})
