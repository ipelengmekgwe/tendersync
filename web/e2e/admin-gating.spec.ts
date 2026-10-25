import { expect, test } from "@playwright/test";
import { signIn } from "./helpers";

/**
 * WP14 acceptance: admin pages are role-gated. An admin creates (or resets) a Viewer through /admin/users' API, then
 * that Viewer sees no Admin, Company or category settings: the links are hidden, the pages answer 404 and the API 403.
 */
const email = process.env.E2E_EMAIL;
const password = process.env.E2E_PASSWORD;
const viewerEmail = "e2e-viewer@tendersync.local";
const viewerPassword = process.env.E2E_VIEWER_PASSWORD || "e2e-viewer-Password-1";

test.describe("role gating", () => {
  test.skip(!email || !password, "E2E_EMAIL and E2E_PASSWORD are not set");

  test("a viewer cannot reach the admin, company or category pages", async ({ page, browser }) => {
    await signIn(page, email!, password!, "/admin/users");
    await expect(page.getByText("Accounts", { exact: true })).toBeVisible();

    const users = (await (await page.request.get("/api/proxy/admin/users")).json()) as { id: string; email: string }[];
    const existing = users.find((u) => u.email === viewerEmail);
    const response = existing
      ? await page.request.put(`/api/proxy/admin/users/${existing.id}`, { data: { displayName: "E2E Viewer", role: "Viewer", disabled: false, newPassword: viewerPassword } })
      : await page.request.post("/api/proxy/admin/users", { data: { email: viewerEmail, displayName: "E2E Viewer", role: "Viewer", password: viewerPassword } });
    expect(response.ok()).toBeTruthy();

    const context = await browser.newContext();
    const viewer = await context.newPage();
    try {
      await signIn(viewer, viewerEmail, viewerPassword, "/");
      const nav = viewer.getByRole("navigation", { name: "Main" });
      if (await nav.isVisible()) {
        await expect(nav.getByRole("link", { name: "Admin" })).toHaveCount(0);
        await expect(nav.getByRole("link", { name: "Company" })).toHaveCount(0);
        await expect(nav.getByRole("link", { name: "Settings" })).toHaveCount(1);
      }

      for (const path of ["/admin/pipeline", "/admin/users", "/admin/audit", "/settings/categories", "/company/profile", "/applications"]) {
        await viewer.goto(path);
        await expect(viewer.getByText("This page could not be found.")).toBeVisible();
      }

      await viewer.goto("/settings/notifications");
      await expect(viewer.getByText("Channels", { exact: true })).toBeVisible();
      await expect(viewer.getByRole("navigation", { name: "Settings sections" }).getByRole("link", { name: "Interest categories" })).toHaveCount(0);

      expect((await viewer.request.get("/api/proxy/admin/users")).status()).toBe(403);
      expect((await viewer.request.get("/api/proxy/interest-categories")).status()).toBe(403);
    } finally {
      await context.close();
    }
  });
});
