import { serverApi } from "@/lib/api/server";
import type { CompanyProfile } from "@/lib/api/types";

/** v1 has one company: the oldest active profile, or null before one is created. */
export async function loadActiveProfile(): Promise<CompanyProfile | null> {
  const client = await serverApi();
  const { data: profiles } = await client.GET("/api/v1/company-profiles");
  const active = (profiles ?? []).find((p) => p.isActive);
  if (!active) {
    return null;
  }
  const { data } = await client.GET("/api/v1/company-profiles/{id}", { params: { path: { id: active.id } } });
  return data ?? null;
}
