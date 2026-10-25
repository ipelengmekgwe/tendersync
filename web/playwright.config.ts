import { defineConfig, devices } from "@playwright/test";

/**
 * End-to-end tests run against a live stack (Compose or `dotnet run` + `npm run dev`): set E2E_BASE_URL (default
 * http://localhost:3000), E2E_EMAIL and E2E_PASSWORD for the seeded admin. Without credentials the specs skip.
 * The global setup seeds the demo tenders first (Worker --seed-demo); E2E_SKIP_SEED=true skips that. The category spec
 * also needs the API to answer classification without a key: start it with Llm__Routing__classify=fake and
 * Llm__FakeAnswers__classify (see README) and set E2E_FAKE_CLASSIFY=true.
 */
export default defineConfig({
  testDir: "./e2e",
  globalSetup: "./e2e/global-setup.ts",
  timeout: 30_000,
  // The specs share live data (the demo match is moved and reset), so the projects run one after the other.
  workers: 1,
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
