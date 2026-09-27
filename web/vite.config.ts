import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

// 開発時は API(localhost:5080)へ中継し、同じオリジンとして扱う。
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/graphql": "http://localhost:5080",
      "/api": "http://localhost:5080",
      "/files": "http://localhost:5080",
    },
  },
  build: {
    // ビルド結果は API が同じオリジンで配る。
    outDir: "../server/src/Serendipity.Api/wwwroot",
    emptyOutDir: true,
  },
  test: {
    environment: "node",
  },
});
