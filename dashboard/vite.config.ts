import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

const api = 'http://localhost:5269'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/stats': api,
      '/events': api,
      '/webhooks': api,
      '/deadletters': api,
      '/hubs': { target: api, ws: true },
    },
  },
})
