// Regenerates src/lib/api/schema.d.ts from the running API (API_BASE_URL, default http://localhost:5000) or from a
// local openapi.json when API_SPEC points at a file. Run: npm run generate:api
import { spawnSync } from "node:child_process";
import { existsSync } from "node:fs";

const spec = process.env.API_SPEC ?? `${(process.env.API_BASE_URL ?? "http://localhost:5000").replace(/\/+$/, "")}/openapi/v1.json`;
if (process.env.API_SPEC && !existsSync(spec)) {
  console.error(`API_SPEC file not found: ${spec}`);
  process.exit(1);
}
console.log(`Generating src/lib/api/schema.d.ts from ${spec}`);
const result = spawnSync(process.platform === "win32" ? "npx.cmd" : "npx", ["openapi-typescript", spec, "-o", "src/lib/api/schema.d.ts"], {
  stdio: "inherit",
  shell: process.platform === "win32",
});
process.exit(result.status ?? 1);
