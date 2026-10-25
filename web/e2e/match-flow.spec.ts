import { type APIRequestContext, type Page, expect, test } from "@playwright/test";

/**
 * WP13 acceptance: new match → open → change status → a requirement's source opens the PDF at the right page.
 * Runs against the demo tender written by the Worker:
 *   dotnet run --project src/TenderSync.Worker -- --seed-demo
 * The test puts the demo match back to New before and after, so it can run again.
 */
const email = process.env.E2E_EMAIL;
const password = process.env.E2E_PASSWORD;
const DEMO_BID = "DEMO/E2E/0001";

interface InboxItem {
  id: string;
  tenderId: string;
  bidNumber: string;
  status: number;
}

async function signIn(page: Page, next: string) {
  await page.goto(`/login?next=${encodeURIComponent(next)}`);
  await page.getByLabel("Email").fill(email!);
  await page.getByLabel("Password").fill(password!);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page).toHaveURL(new RegExp(`${next.replace(/[/?]/g, "\\$&")}$`));
}

async function findDemoMatch(request: APIRequestContext): Promise<InboxItem | undefined> {
  const response = await request.get("/api/proxy/matches?PageSize=200&Sort=2");
  expect(response.ok()).toBeTruthy();
  const inbox = (await response.json()) as { items: InboxItem[] };
  return inbox.items.find((m) => m.bidNumber === DEMO_BID);
}

async function resetToNew(request: APIRequestContext, id: string) {
  const response = await request.patch(`/api/proxy/matches/${id}`, { data: { status: 0, notes: "", dismissReason: null } });
  expect(response.ok()).toBeTruthy();
}

test.describe("matches inbox to PDF source", () => {
  test.skip(!email || !password, "E2E_EMAIL and E2E_PASSWORD are not set");

  test("new match → open → change status → requirement source opens the PDF at its page", async ({ page }) => {
    await signIn(page, "/matches");

    const demo = await findDemoMatch(page.request);
    test.skip(!demo, `No ${DEMO_BID} match: run "dotnet run --project src/TenderSync.Worker -- --seed-demo" first`);
    if (demo!.status !== 0) {
      await resetToNew(page.request, demo!.id);
    }

    try {
      // New match in the inbox.
      await page.reload();
      await expect(page.getByRole("link", { name: "New", exact: false }).first()).toHaveAttribute("aria-current", "page");
      const row = page.getByTestId("match-row").filter({ hasText: DEMO_BID });
      await expect(row).toBeVisible();
      await expect(row.getByTestId("match-status")).toHaveText("New");

      // Open it.
      await row.getByRole("link", { name: /Network support and managed IT services/ }).click();
      await expect(page).toHaveURL(new RegExp(`/tenders/${demo!.tenderId}$`));
      await expect(page.getByRole("heading", { level: 1 })).toContainText("Network support and managed IT services");
      await expect(page.getByTestId("verdict")).toHaveText("Partial");

      // Change status, and it sticks.
      const matchCard = page.locator("#match");
      await matchCard.getByRole("button", { name: "Start review" }).click();
      await expect(matchCard.getByTestId("match-status")).toHaveText("Reviewing");
      await page.reload();
      await expect(page.locator("#match").getByTestId("match-status")).toHaveText("Reviewing");

      // A requirement's source opens the PDF at its page.
      await page.getByRole("link", { name: /^Bid Document DEMO-E2E-0001\.pdf p\. 2 \(open the document at page 2\)$/ }).first().click();
      await expect(page).toHaveURL(/\/documents\/[^/?]+\?page=2&quote=/);
      await expect(page.getByTestId("source-quote")).toContainText("Central Supplier Database");
      await expect(page.getByTestId("pdf-page-indicator")).toHaveText("Page 2 of 3", { timeout: 15_000 });
      const textLayer = page.locator(".react-pdf__Page__textContent");
      await expect(textLayer).toContainText("SECTION 2: MANDATORY REQUIREMENTS", { timeout: 15_000 });
      await expect(textLayer.locator("mark").first()).toContainText("Central Supplier Database");

      // Paging keeps the address bar in step.
      await page.getByRole("button", { name: "Next page" }).click();
      await expect(page.getByTestId("pdf-page-indicator")).toHaveText("Page 3 of 3");
      await expect(page).toHaveURL(/page=3/);
    } finally {
      await resetToNew(page.request, demo!.id);
    }
  });
});
