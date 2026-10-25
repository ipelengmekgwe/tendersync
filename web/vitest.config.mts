import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import path from "node:path";

export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test/setup.ts"],
    include: ["src/**/*.test.{ts,tsx}"],
    css: false,
    // npm run test:coverage (CI): v8 coverage of the app code; generated types and shadcn primitives are left out.
    coverage: {
      provider: "v8",
      include: ["src/**/*.{ts,tsx}"],
      exclude: ["src/**/*.test.{ts,tsx}", "src/test/**", "src/lib/api/schema.d.ts", "src/components/ui/**"],
      reporter: ["text-summary", "json-summary", "cobertura", "html"],
      reportsDirectory: "coverage",
    },
  },
  resolve: {
    alias: { "@": path.resolve(__dirname, "src") },
  },
});
