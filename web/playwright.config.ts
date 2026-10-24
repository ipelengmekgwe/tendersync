import { defineConfig, devices } from "@playwright/test";

/**
 * End-to-end tests run against a live stack (Compose or `dotnet run` + `npm run dev`): set E2E_BASE_URL (default
 * http://localhost:3000), E2E_EMAIL and E2E_PASSWORD for the seeded admin. Without credentials the specs skip.
 */
export default defineConfig({
  testDir: "./e2e",
  timeout: 30_000,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["github"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:3000",
    trace: "retain-on-failure",
  },
  projects: [
    { name: "chromium", use: { ...devices["Desktop Chrome"] } },
    { name: "mobile", use: { ...devices["Pixel 7"] } },
  ],
});
