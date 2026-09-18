# Folio integration

- Active React source: `FolioApp/src`.
- Production build: `Folio.Api/wwwroot`.
- Existing API: ASP.NET Core 8, Entity Framework, SQL Server.
- Catalog contract: `GET /api/books?page=N&pageSize=100`, returning `{ items, page, pageSize, totalItems, totalPages }`.
- Book DTO and genre enum (1–8) mapping: `FolioApp/src/lib.js`.
- Vite proxies `/api` to HTTPS port 7201.
- ASP.NET serves the build and has SPA fallback routing, with a separate JSON 404 fallback for unknown API paths.
- Shelf, imported text, progress, notes, goals, and Shield settings are browser-local. No new database schema or authentication flow is introduced.
- API failure displays the seed catalog as a labeled preview. The reader remains usable with the original story and imported text.

Run instructions and limitations: [FolioApp/README.md](FolioApp/README.md).

The old root `wwwroot` output and `folio-client` starter are retained but are not the active frontend.
