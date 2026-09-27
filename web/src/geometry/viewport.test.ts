import { describe, expect, it } from "vitest";
import { bounds, fitView, toWorld, zoomAround, zoomLevel } from "./viewport";

describe("viewport", () => {
  it("keeps the point under the finger fixed while zooming", () => {
    const v = { tx: 30, ty: -20, k: 2 };
    const before = toWorld(v, 150, 90);
    const after = toWorld(zoomAround(v, 150, 90, 5), 150, 90);
    expect(after.x).toBeCloseTo(before.x, 9);
    expect(after.y).toBeCloseTo(before.y, 9);
  });

  it("fits the whole city on screen", () => {
    const b = bounds([
      { x: -100, y: 0, r: 10 },
      { x: 100, y: 50, r: 10 },
    ]);
    const v = fitView(b, { width: 400, height: 800 }, 20);
    const tl = toWorld(v, 20, 400);
    expect(tl.x).toBeCloseTo(-110, 6);
  });

  it("switches level by how big a base mark looks on screen", () => {
    expect(zoomLevel(1, 10)).toBe("far");
    expect(zoomLevel(3, 10)).toBe("mid");
    expect(zoomLevel(8, 10)).toBe("near");
  });
});
