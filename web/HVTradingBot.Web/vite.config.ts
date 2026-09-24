import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The production build is served by the API from its wwwroot, so the dashboard and API share one origin.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../../src/HVTradingBot.Api/wwwroot",
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      "/api": "http://127.0.0.1:5080",
      "/health": "http://127.0.0.1:5080",
      "/hubs": { target: "http://127.0.0.1:5080", ws: true },
    },
  },
});
