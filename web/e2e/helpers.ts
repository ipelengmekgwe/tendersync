import { type Page, expect } from "@playwright/test";

/** Signs in through the login page and waits for the redirect to `next`. */
export async function signIn(page: Page, email: string, password: string, next = "/") {
  await page.goto(`/login?next=${encodeURIComponent(next)}`);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page).toHaveURL(new RegExp(`${next.replace(/[/?.]/g, "\$&")}$`));
}
