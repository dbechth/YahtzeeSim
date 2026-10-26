// Minimal typings for Node's built-in SQLite and Vite's import.meta.glob, used only by the API tests.
declare module "node:sqlite" {
  export class DatabaseSync {
    constructor(path: string);
    exec(sql: string): void;
    prepare(sql: string): {
      get(...params: unknown[]): Record<string, unknown> | undefined;
      all(...params: unknown[]): Record<string, unknown>[];
      run(...params: unknown[]): { changes: number | bigint; lastInsertRowid: number | bigint };
    };
  }
}

interface ImportMeta {
  glob(pattern: string, options: { query: "?raw"; import: "default"; eager: true }): Record<string, string>;
}
