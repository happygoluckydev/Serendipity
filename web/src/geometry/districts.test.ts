import { describe, expect, it } from "vitest";
import { computeDistricts } from "./districts";

describe("computeDistricts", () => {
  it("colours each block by its most common genre, never a blend", () => {
    const { districts, genreOf } = computeDistricts(
      [
        { x: 1, y: 1, genre: "A" },
        { x: 2, y: 2, genre: "B" },
        { x: 3, y: 3, genre: "B" },
        { x: 55, y: 1, genre: "A" },
      ],
      50,
      ["A", "B"],
    );
    expect(genreOf).toEqual(["B", "B", "B", "A"]);
    expect(districts.find((d) => d.key === "0,0")).toMatchObject({ genre: "B", count: 3 });
    expect(districts.find((d) => d.key === "1,0")).toMatchObject({ genre: "A", count: 1 });
  });

  it("breaks ties by the fixed genre order so colours are stable", () => {
    const { genreOf } = computeDistricts(
      [
        { x: 1, y: 1, genre: "B" },
        { x: 2, y: 2, genre: "A" },
      ],
      50,
      ["A", "B"],
    );
    expect(genreOf).toEqual(["A", "A"]);
  });
});
