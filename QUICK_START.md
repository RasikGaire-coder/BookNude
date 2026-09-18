# Run Folio

The active React frontend is in **FolioApp**. The ASP.NET Core API is in **Folio.Api**.

## Development

Open two terminals in this solution folder.

API:

```powershell
dotnet run --project Folio.Api --launch-profile https
```

React:

```powershell
cd FolioApp
npm install
npm run dev
```

Open http://localhost:5173 (or the address Vite prints). API calls are proxied to https://localhost:7201.

## Serve the built frontend from the API

```powershell
cd FolioApp
npm run build
cd ..
dotnet run --project Folio.Api --launch-profile https
```

Open https://localhost:7201. The React build is in `Folio.Api/wwwroot`.

The SQL Server instance configured in `Folio.Api/appsettings.json` must be available with the existing migrations applied for the live catalog. If it is unavailable, the frontend shows a clearly labeled preview catalog.

## Explore

- Search and filter titles in **Discover**.
- Save titles to **My bookshelf**, or import a `.txt` book.
- Try the free original story through **Step into the reading room**.
- Enter remembered clues in **Book Detective**.
- Save a note for a future date in **Time capsules**.
- Control description and note visibility through **Spoiler Shield**.

Personal reading data stays in the current browser. The existing API supplies metadata, not full book texts. See [FolioApp/README.md](FolioApp/README.md) for details and checks.
