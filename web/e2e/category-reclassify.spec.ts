import { type APIRequestContext, expect, test } from "@playwright/test";
import { signIn } from "./helpers";

/**
 * WP14 acceptance: category edit → reclassify → match changes. Adds "quokka" to APP_SUPPORT, which only the seeded
 * DEMO/E2E/0002 contains, reclassifies that tender and expects a New match. The model stage must answer without a key:
 * run the API with Llm__Routing__classify=fake and a Llm__FakeAnswers__classify answer (README), then set
 * E2E_FAKE_CLASSIFY=true. The keyword is removed again afterwards.
 */
const email = process.env.E2E_EMAIL;
const password = process.env.E2E_PASSWORD;
const KEYWORD = "quokka";
const TENDER = "DEMO/E2E/0002";

interface Category {
  id: string;
  code: string;
  name: string;
  description: string | null;
  keywords: string[];
  negativeKeywords: string[];
  weight: number;
  enabled: boolean;
}

async function appSupport(request: APIRequestContext): Promise<Category> {
  const response = await request.get("/api/proxy/interest-categories");
  expect(response.ok()).toBeTruthy();
  return ((await response.json()) as Category[]).find((c) => c.code === "APP_SUPPORT")!;
}

async function setKeywords(request: APIRequestContext, category: Category, keywords: string[]) {
  const { name, description, negativeKeywords, weight, enabled } = category;
  const response = await request.put(`/api/proxy/interest-categories/${category.id}`, { data: { name, description, keywords, negativeKeywords, weight, enabled } });
  expect(response.ok()).toBeTruthy();
}

test.describe("interest categories", () => {
  test.skip(!email || !password, "E2E_EMAIL and E2E_PASSWORD are not set");
  test.skip(process.env.E2E_FAKE_CLASSIFY !== "true", "needs the API's classify purpose routed to the fake provider (E2E_FAKE_CLASSIFY=true)");

  test("category edit → reclassify → a match appears", async ({ page }) => {
    await signIn(page, email!, password!, "/settings/categories");
    const before = await appSupport(page.request);
    if (before.keywords.includes(KEYWORD)) {
      await setKeywords(page.request, before, before.keywords.filter((k) => k !== KEYWORD));
      await page.reload();
    }

    try {
      // Edit the category in the UI.
      await page.getByRole("button", { name: "Edit APP_SUPPORT" }).click();
      const dialog = page.getByRole("dialog");
      const keywords = dialog.getByLabel("Keywords", { exact: true });
      await keywords.fill(`${await keywords.inputValue()}\n${KEYWORD}`);
      await dialog.getByRole("button", { name: "Save" }).click();
      await expect(dialog).toBeHidden();
      await expect(page.getByTestId("category-row").filter({ hasText: "APP_SUPPORT" })).toContainText(KEYWORD);

      // The keyword stage now picks the tender up.
      await page.getByLabel("Bid number, title words or tender id").fill(TENDER);
      await page.getByRole("button", { name: "Test", exact: true }).click();
      const result = page.getByTestId("prefilter-result");
      await expect(result.getByTestId("prefilter-verdict")).toHaveText("Candidate: goes to the model");
      await expect(result).toContainText(`${KEYWORD} (title +3)`);

      // Reclassify: the (fake) model scores it and a match is created.
      await result.getByRole("button", { name: "Reclassify this tender" }).click();
      await expect(page.getByTestId("reclassify-outcome")).toContainText("Relevance 85 (APP_SUPPORT)");
      await page.getByTestId("reclassify-outcome").getByRole("link", { name: "Open the tender" }).click();
      await expect(page.getByRole("heading", { level: 1 })).toContainText("quokka feed pellets");
      await expect(page.locator("#match").getByTestId("match-status")).toHaveText("New");
    } finally {
      const after = await appSupport(page.request);
      await setKeywords(page.request, after, after.keywords.filter((k) => k !== KEYWORD));
    }
  });
});
