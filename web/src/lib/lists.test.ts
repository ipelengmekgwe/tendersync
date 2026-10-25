import { describe, expect, it } from "vitest";
import { dateInputToIso, expiryState, formatList, isoToDateInput, parseList } from "@/lib/lists";
import { problemMessage, unwrap } from "@/lib/api/problem";

describe("list and date helpers", () => {
  it("parses comma or line separated lists without blanks or case-insensitive duplicates", () => {
    expect(parseList(" Azure, .NET\n\nazure ,SQL Server\n")).toEqual(["Azure", ".NET", "SQL Server"]);
    expect(formatList(["a", "b"])).toBe("a, b");
    expect(formatList(null)).toBe("");
  });

  it("stores dates as midnight in South Africa and reads them back as the same calendar day", () => {
    expect(dateInputToIso("2026-11-10")).toBe("2026-11-10T00:00:00+02:00");
    expect(dateInputToIso("")).toBeNull();
    expect(isoToDateInput("2026-11-09T22:00:00Z")).toBe("2026-11-10");
    expect(isoToDateInput(null)).toBe("");
  });

  it("classifies expiry for the badges", () => {
    const now = new Date("2026-10-25T08:00:00Z");
    expect(expiryState(null, now)).toBe("none");
    expect(expiryState("2026-10-24T00:00:00Z", now)).toBe("expired");
    expect(expiryState("2026-11-10T00:00:00Z", now)).toBe("soon");
    expect(expiryState("2027-03-01T00:00:00Z", now)).toBe("valid");
  });
});

describe("problem details", () => {
  it("prefers field errors, then detail, then title, then the status", () => {
    expect(problemMessage({ title: "One or more validation errors occurred.", errors: { Code: ["Code: 2 to 40 letters."], Name: ["Name is required."] } })).toBe(
      "Code: 2 to 40 letters. Name is required.",
    );
    expect(problemMessage({ title: "Conflict", detail: "This is the last enabled admin." })).toBe("This is the last enabled admin.");
    expect(problemMessage(undefined, 502)).toBe("The API responded 502.");
  });

  it("unwraps data and throws the problem message on failure", () => {
    expect(unwrap({ data: 1, response: new Response(null, { status: 200 }) })).toBe(1);
    expect(unwrap({ data: undefined, response: new Response(null, { status: 204 }) })).toBeUndefined();
    expect(() => unwrap({ error: { title: "Not found" }, response: new Response(null, { status: 404 }) })).toThrow("Not found");
  });
});
