import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Library mode: one self-contained ES module (React bundled, CSS inlined into the Shadow DOM via ?inline).
export default defineConfig({
  plugins: [react()],
  define: {
    "process.env.NODE_ENV": JSON.stringify("production"),
  },
  build: {
    target: "es2022",
    outDir: "dist",
    emptyOutDir: true,
    sourcemap: false,
    minify: true,
    cssCodeSplit: false,
    lib: {
      entry: "src/index.ts",
      formats: ["es"],
      fileName: () => "commonroom-chat.js",
    },
  },
});
