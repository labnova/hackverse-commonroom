import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Port 5173 must match the Cognito app client callback/logout URLs.
export default defineConfig({
  plugins: [react()],
  server: { port: 5173, strictPort: true },
  preview: { port: 5173, strictPort: true },
});
