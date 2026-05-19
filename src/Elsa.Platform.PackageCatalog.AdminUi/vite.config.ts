import path from "node:path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  base: "/admin/",
  plugins: [react()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src")
    }
  },
  server: {
    port: 5173,
    proxy: {
      "/api": {
        target: process.env.VITE_CATALOG_API_PROXY_TARGET ?? "http://localhost:5000",
        changeOrigin: true
      }
    }
  }
});
