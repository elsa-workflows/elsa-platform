import path from "node:path";
import react from "@vitejs/plugin-react";
import { defineConfig, loadEnv } from "vite";

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, __dirname, "");
  const catalogApiProxyTarget = env.CATALOG_API_PROXY_TARGET ?? "http://localhost:5220";
  const portValue = process.env.PORT ?? env.PORT ?? "5173";
  const port = Number(portValue);
  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    throw new Error(`Invalid PORT "${portValue}": expected a TCP port between 1 and 65535`);
  }

  let catalogApiProxyOrigin: string;
  try {
    catalogApiProxyOrigin = new URL(catalogApiProxyTarget).origin;
    if (catalogApiProxyOrigin === "null") {
      throw new Error("include a URL scheme such as http:// or https://");
    }
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    throw new Error(`Invalid CATALOG_API_PROXY_TARGET "${catalogApiProxyTarget}": ${message}`);
  }
  const devIdentityIssuer = env.CATALOG_DEV_IDENTITY_ISSUER ?? "https://elsaworkflows.io";
  const devIdentitySubject = env.CATALOG_DEV_IDENTITY_SUBJECT ?? "local-admin";
  const devIdentityEmail = env.CATALOG_DEV_IDENTITY_EMAIL ?? "local-admin@example.test";
  const devIdentityName = env.CATALOG_DEV_IDENTITY_NAME ?? "Local Admin";

  return {
    base: "/admin/",
    plugins: [react()],
    resolve: {
      alias: {
        "@": path.resolve(__dirname, "./src")
      }
    },
    server: {
      port,
      strictPort: true,
      proxy: {
        "/api": {
          target: catalogApiProxyTarget,
          changeOrigin: true,
          configure: (proxy) => {
            proxy.on("proxyReq", (proxyReq) => {
              proxyReq.setHeader("Origin", catalogApiProxyOrigin);
              proxyReq.setHeader("X-Catalog-Identity-Issuer", devIdentityIssuer);
              proxyReq.setHeader("X-Catalog-Identity-Subject", devIdentitySubject);
              proxyReq.setHeader("X-Catalog-Identity-Email", devIdentityEmail);
              proxyReq.setHeader("X-Catalog-Identity-Name", devIdentityName);
            });
          }
        }
      }
    }
  };
});
