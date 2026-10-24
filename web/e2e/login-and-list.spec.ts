import { expect, test } from "@playwright/test";

const email = process.env.E2E_EMAIL;
const password = process.env.E2E_PASSWORD;

test.describe("login and tender list", () => {
  test.skip(!email || !password, "E2E_EMAIL and E2E_PASSWORD are not set");

  test("redirects to login, signs in, lists tenders, signs out", async ({ page }) => {
    await page.goto("/tenders");
    await expect(page).toHaveURL(/\/login\?next=%2Ftenders/);

    await page.getByLabel("Email").fill(email!);
    await page.getByLabel("Password").fill(password!);
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(page).toHaveURL(/\/tenders$/);
    await expect(page.getByRole("heading", { name: "Tenders" })).toBeVisible();
    await expect(page.getByText(/\d+ tenders?|No tenders match/)).toBeVisible();

    await page.getByRole("textbox", { name: "Search" }).fill("supply");
    await page.getByRole("button", { name: "Search" }).click();
    await expect(page).toHaveURL(/q=supply/);

    await page.getByRole("button", { name: "Account menu" }).click();
    await page.getByRole("menuitem", { name: "Sign out" }).click();
    await expect(page).toHaveURL(/\/login/);
  });

  test("wrong password is reported without leaving the page", async ({ page }) => {
    test.skip(process.env.E2E_DEV_AUTH_BYPASS === "true", "the API accepts any credentials (DevAuth:AllowAnyCredentials)");
    await page.goto("/login");
    await page.getByLabel("Email").fill(email!);
    await page.getByLabel("Password").fill("definitely-wrong-password");
    await page.getByRole("button", { name: "Sign in" }).click();
    await expect(page.getByTestId("login-error")).toContainText("Email or password is wrong");
  });
});
