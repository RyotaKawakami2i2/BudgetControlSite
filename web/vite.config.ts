import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// 業務の画面のビルド（詳細設計書 7.1）。出力先はアプリの wwwroot/app で、ファイル名に内容のハッシュを付ける。
// 認証の画面（Blazor）用のスタイル account.css は、名前を固定して出力する（/app/account.css）。
const api = 'http://localhost:5080';

// 開発サーバー（Vite）から API へ中継するときは、Origin をアプリの URL にする（サーバーは Origin を確かめるため）
const proxy = { target: api, changeOrigin: true, headers: { origin: api } };

export default defineConfig({
  base: '/app/',
  plugins: [react()],
  build: {
    outDir: '../src/TaskYojitsu.Web/wwwroot/app',
    emptyOutDir: true,
    sourcemap: false,
    // 社内ネットワークで使う業務の画面のため、1 つのファイルにまとめる（gzip で約 180KB）
    chunkSizeWarningLimit: 800,
    rolldownOptions: {
      input: {
        main: 'index.html',
        account: 'src/styles/account.css',
      },
      output: {
        assetFileNames: (info) =>
          info.names.includes('account.css') ? 'account.css' : 'assets/[name]-[hash][extname]',
      },
    },
  },
  server: {
    host: '0.0.0.0',
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': proxy,
      '/account': proxy,
      '/js': proxy,
      '/favicon.svg': proxy,
      '/app/account.css': proxy,
    },
  },
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
});
