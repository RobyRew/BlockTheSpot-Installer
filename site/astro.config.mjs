import { defineConfig } from 'astro/config';
export default defineConfig({
  site: 'https://robyrew.github.io',
  base: '/BlockTheSpot-Installer',
  output: 'static',
  trailingSlash: 'always',
  devToolbar: { enabled: false },
});
