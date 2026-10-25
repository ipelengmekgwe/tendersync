import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MatchStatusControl } from "@/components/matches/match-status-control";

const patch = vi.fn();
const refresh = vi.fn();

vi.mock("next/navigation", () => ({ useRouter: () => ({ refresh }) }));
vi.mock("@/lib/api/client", () => ({ api: { PATCH: (...args: unknown[]) => patch(...args) } }));

function renderControl(canEdit: boolean, status = 0) {
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MatchStatusControl canEdit={canEdit} match={{ id: "m1", status, notes: null, dismissReason: null }} />
    </QueryClientProvider>,
  );
}

const updated = (status: number, dismissReason: string | null = null) => ({
  data: { id: "m1", tenderId: "t1", companyProfileId: "p1", relevance: 80, status, notes: null, dismissReason, dismissedAt: null, notifiedAt: null },
  error: undefined,
  response: new Response(null, { status: 200 }),
});

describe("MatchStatusControl", () => {
  beforeEach(() => {
    patch.mockReset();
    refresh.mockReset();
  });

  it("shows viewers the status without any controls", () => {
    renderControl(false);

    expect(screen.getByTestId("match-status")).toHaveTextContent("New");
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("moves the match with one click and refreshes the page data", async () => {
    patch.mockResolvedValue(updated(1));
    renderControl(true);

    await userEvent.click(screen.getByRole("button", { name: "Start review" }));

    await waitFor(() => expect(screen.getByTestId("match-status")).toHaveTextContent("Reviewing"));
    expect(patch).toHaveBeenCalledWith("/api/v1/matches/{id}", { params: { path: { id: "m1" } }, body: { status: 1, notes: null, dismissReason: null } });
    expect(refresh).toHaveBeenCalled();
  });

  it("asks for a reason before dismissing", async () => {
    patch.mockResolvedValue(updated(2, "Outside our provinces"));
    renderControl(true);

    await userEvent.click(screen.getByRole("button", { name: "Dismiss" }));
    const dialog = await screen.findByRole("dialog");
    const confirm = screen.getAllByRole("button", { name: "Dismiss" }).at(-1)!;
    expect(confirm).toBeDisabled();
    expect(patch).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole("button", { name: "Outside our provinces" }));
    await userEvent.click(confirm);

    await waitFor(() => expect(patch).toHaveBeenCalledWith("/api/v1/matches/{id}", expect.objectContaining({ body: { status: 2, notes: null, dismissReason: "Outside our provinces" } })));
    await waitFor(() => expect(dialog).not.toBeInTheDocument());
    expect(screen.getByTestId("match-status")).toHaveTextContent("Dismissed");
  });

  it("shows the API's validation message when a change is refused", async () => {
    patch.mockResolvedValue({ data: undefined, error: { title: "One or more validation errors occurred.", errors: { DismissReason: ["A dismiss reason is required."] } }, response: new Response(null, { status: 400 }) });
    renderControl(true);

    await userEvent.click(screen.getByRole("button", { name: "Applying" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("A dismiss reason is required.");
  });
});
