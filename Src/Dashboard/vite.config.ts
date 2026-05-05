import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    // Dev mode: proxy /api calls to the .NET Host
    proxy: {
      '/api': 'http://localhost:5000',
    },
  },
  build: {
    // Production: output goes into Host/wwwroot/ and is served as static assets
    outDir: '../ApiHost/wwwroot',
    emptyOutDir: true,
  },
})
