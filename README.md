# Text2SQL (SQL Server) - React + ASP.NET Core + OpenRouter + Docker

A simple MVP web app where a user types a request in plain English and the app:

1. generates a **T-SQL** query for SQL Server using an LLM (OpenRouter)
2. executes the query on a SQL Server database
3. displays both the generated SQL and the results in the UI

## Tech Stack

- **Frontend:** React + TypeScript (Vite)
- **Backend:** ASP.NET Core Minimal API
- **AI:** OpenRouter (LLM)
- **OpenRouter Model:** mistralai/mistral-7b-instruct:free
- **Database:** SQL Server (Docker container)
- **DevOps:** Docker Compose
- **Version Control:** Git + GitHub

## Project Structure

.
├── backend/ <br>
│ └── Text2Sql.Api/ # ASP.NET Core Minimal API<br>
├── frontend/<br>
│ └── text2sql-web/ # React UI<br>
├── db/<br>
│ └── init.sql # DB seed script (Orders table)<br>
├── docker-compose.yml # SQL Server (+ API container if enabled)<br>
└── README.md<br>

## How It Works (Request Flow)

1. User enters text in the React UI (example: "Show total sales by month for 2025")
2. React calls the backend endpoint: `POST /api/generate-and-run`
3. Backend calls OpenRouter to generate **T-SQL**
4. Backend validates the SQL (SELECT-only guardrails)
5. Backend runs SQL against SQL Server
6. Backend returns JSON:

```json
{
  "sql": "...",
  "columns": ["..."],
  "rows": [[...], [...]]
}
```

7. React displays SQL + results table

## Run Locally (Dev)

1. Start SQL Server (Docker)<br>
   Make sure Docker Desktop is running.

```
docker compose up -d
```

2. Seed database<br>
   This creates Text2SqlDemo and dbo.Orders with sample data.

```
Get-Content .\db\init.sql | docker exec -i text2sql-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "<YOUR_PASSWORD>" -C
```

3. Run Backend

```
cd backend/Text2Sql.Api
dotnet run --urls http://localhost:5000
```

4. Run Frontend

```
cd frontend/text2sql-web
npm install
npm run dev
```

Open: http://localhost:5173

## Configuration

### OpenRouter

Set API key and model using environment variables or user-secrets.<br>
Example (Docker/.env):

```
OPENROUTER_API_KEY=your_key_here
MSSQL_SA_PASSWORD=your_password_here
```

### SQL Server Connection

Local machine access: Server=localhost,1433<br>
Inside Docker access: Server=sqlserver,1433<br>

### API Endpoints

GET /api/test-db - checks DB connectivity<br>
POST /api/generate-sql - generates SQL only<br>
POST /api/generate-and-run - generates SQL and executes it, returns results<br>

### Notes / Limitations

This MVP supports a demo schema: Text2SqlDemo.dbo.Orders<br>
LLM "free" models can become unavailable; switching models may be required<br>
SQL execution is guarded (SELECT-only) but should be hardened further for production<br>

### Future Improvements

Add "Connect your DB" feature (schema introspection)<br>
Authentication and user workspaces<br>
Better SQL parsing with ScriptDom<br>
Pagination and sorting in UI<br>
Deploy frontend + backend + managed database<br>
