import path from "node:path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src")
    }
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test/setupTests.ts"],
    css: true,
    // Must stay above the asyncUtilTimeout configured in setupTests, so a slow findBy* reports the
    // element it was waiting for rather than an opaque test timeout.
    testTimeout: 20_000
  }
});
