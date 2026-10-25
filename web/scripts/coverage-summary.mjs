// Prints the Vitest coverage totals (coverage/coverage-summary.json) as a Markdown table for the GitHub job summary.
// Run after `npm run test:coverage`: node scripts/coverage-summary.mjs >> "$GITHUB_STEP_SUMMARY"
import { readFileSync } from "node:fs";

const { total } = JSON.parse(readFileSync(new URL("../coverage/coverage-summary.json", import.meta.url), "utf8"));
const rows = ["lines", "statements", "functions", "branches"].map((k) => `| ${k} | ${total[k].pct}% | ${total[k].covered} of ${total[k].total} |`);
console.log(["## Web unit test coverage", "", "| Metric | Covered | Count |", "|---|---|---|", ...rows].join("\n"));
