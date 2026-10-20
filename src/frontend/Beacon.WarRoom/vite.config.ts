import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
// VITE_BASE is the path the app is served under: "/" locally and in the nginx image, "/Beacon/"
// on GitHub Pages (https://<user>.github.io/Beacon/).
export default defineConfig({
  base: process.env.VITE_BASE || '/',
  plugins: [react()],
})
