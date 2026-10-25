import { spawnSync } from "node:child_process";
import path from "node:path";

/**
 * Seeds the demo tenders the specs use (Worker --seed-demo, Development only) so every run starts from the same state:
 * DEMO/E2E/0001 with a New match, DEMO/E2E/0002 unclassified. Skipped with E2E_SKIP_SEED=true or without credentials.
 * Tries the existing build first, so a Worker already running in Visual Studio does not block the build output.
 */
export default function globalSetup() {
  if (process.env.E2E_SKIP_SEED === "true" || !process.env.E2E_EMAIL) {
    return;
  }
  const project = path.resolve(__dirname, "../../src/TenderSync.Worker");
  const run = (extra: string[]) =>
    spawnSync("dotnet", ["run", "--project", project, ...extra, "--", "--seed-demo"], { stdio: "pipe", encoding: "utf8", timeout: 240_000, shell: process.platform === "win32" });

  let result = run(["--no-build"]);
  if (result.status !== 0) {
    result = run([]);
  }
  if (result.status !== 0) {
    throw new Error(`Demo seed failed (exit ${result.status}). Run it by hand to see why:\n  dotnet run --project src/TenderSync.Worker -- --seed-demo\n${result.stdout}\n${result.stderr}`);
  }
}
