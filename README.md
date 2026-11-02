# SiteLedger

[![CI](https://github.com/harshakallem22/siteledger/actions/workflows/ci.yml/badge.svg)](https://github.com/harshakallem22/siteledger/actions/workflows/ci.yml)

A REST API for construction project cost tracking: projects and budgets, labor timesheets, expenses, an approval workflow, and budget-versus-actual reports.

Built with **C# / .NET 10, ASP.NET Core, EF Core 10 and SQL Server**, deployed to **Azure App Service**.

## What it does

- **Projects** have a budget and a lifecycle (Draft → Active → Completed).
- **Timesheets** record hours for an employee. The employee's hourly rate is copied at submission, so later rate changes never rewrite past costs.
- **Expenses** record materials, equipment, travel and other costs.
- **Approvals:** an Approver approves or rejects each timesheet or expense once. Every decision writes an audit record in the same database transaction.
- **Reports** count **approved** items only: budget vs. actual per project, and a portfolio summary across all projects.

Roles: **ProjectManager** (create projects, submit time and expenses), **Approver** (approve/reject), **Viewer** (read only). All endpoints require a JWT.

## Architecture

```
src/
  SiteLedger.Domain          entities and business rules (no database or web code)
  SiteLedger.Application     services, request/response models
  SiteLedger.Infrastructure  EF Core, SQL Server mappings, migrations, demo data seeder
  SiteLedger.Api             controllers, JWT auth, error handling, logging, health checks
tests/
  SiteLedger.UnitTests         business rules
  SiteLedger.IntegrationTests  real HTTP calls against a real SQL Server
tools/
  SiteLedger.TokenTool       issues local development tokens
  perf/                      k6 load test
```

## Run it locally

**You need:** .NET 10 SDK and a SQL Server database. Either run SQL Server in Docker on an x86-64 machine (below), or use any SQL Server or Azure SQL database you have.

> SQL Server container images don't run on Apple Silicon Macs. There, use Azure SQL or another SQL Server.

**1. Get the code and tools**
```bash
git clone https://github.com/harshakallem22/siteledger.git
cd siteledger
dotnet tool restore
```

**2. (Optional) Start SQL Server in Docker** (x86-64 only)
```bash
cp .env.example .env        # then set MSSQL_SA_PASSWORD and JWT_SIGNING_KEY in .env
docker compose up -d sqlserver
```

**3. Store your settings locally** (kept outside the repository)
```bash
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost,1433;Database=SiteLedger;User ID=sa;Password=<your password>;TrustServerCertificate=True" --project src/SiteLedger.Api
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)" --project src/SiteLedger.Api
```

**4. Create the database and load demo data** (100 projects, 6,000 timesheets, 4,000 expenses)
```bash
dotnet dotnet-ef database update --project src/SiteLedger.Infrastructure --startup-project src/SiteLedger.Api
dotnet run --project src/SiteLedger.Api -- seed
```

**5. Start the API**
```bash
dotnet run --project src/SiteLedger.Api
```
It listens on `http://localhost:5201`. Check it with `curl http://localhost:5201/health/ready`.

**6. Get a token and call it** (in a second terminal)
```bash
TOKEN=$(dotnet run --project tools/SiteLedger.TokenTool -- --role ProjectManager --sub demo-pm)

curl -H "Authorization: Bearer $TOKEN" "http://localhost:5201/api/v1/projects?pageSize=5"

curl -X POST http://localhost:5201/api/v1/projects \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"projectCode":"CON-2026-001","name":"Equipment Bay Upgrade","budgetUsd":250000}'
```
Use `--role Approver` or `--role Viewer` for the other roles. The OpenAPI description is at `http://localhost:5201/openapi/v1.json` (Development only).

## API

All routes start with `/api/v1`. Errors use the standard `application/problem+json` format.

| Area | Method | Route | Role |
|---|---|---|---|
| Projects | POST | `/projects` | ProjectManager |
| | GET | `/projects?status=&page=&pageSize=` | any |
| | GET | `/projects/{id}` | any |
| | PATCH | `/projects/{id}` | ProjectManager |
| Timesheets | POST | `/projects/{id}/timesheets` | ProjectManager |
| | GET | `/projects/{id}/timesheets` | any |
| | POST | `/timesheets/{id}/approve` | Approver |
| | POST | `/timesheets/{id}/reject` | Approver |
| Expenses | POST | `/projects/{id}/expenses` | ProjectManager |
| | GET | `/projects/{id}/expenses` | any |
| | POST | `/expenses/{id}/approve` | Approver |
| | POST | `/expenses/{id}/reject` | Approver |
| Reports | GET | `/projects/{id}/reports/budget-vs-actual` | any |
| | GET | `/reports/portfolio-summary` | any |

Also: `GET /health/live` and `GET /health/ready` (no token needed).

## Tests

```bash
dotnet test
```
- **Unit tests** need nothing else.
- **Integration tests** need SQL Server. With Docker running on an x86-64 machine, they start a SQL Server container automatically. Otherwise set `SITELEDGER_TEST_SQL` to a connection string for a database whose name contains `test` (the tests delete its data).

## Docker

```bash
docker build -t siteledger-api .
docker run -p 8080:8080 \
  -e ConnectionStrings__Default="<connection string>" \
  -e Jwt__SigningKey="<at least 32 characters>" \
  siteledger-api
```
`docker compose up -d --build` runs SQL Server and the API together (x86-64 only; apply the migrations first as in step 4).

## Deployment

Every push to `main` that passes CI is published to **Azure App Service** (Free F1, Windows) backed by an **Azure SQL** database. The pipeline then checks `/health/live`, `/health/ready` (database reachable) and that the API rejects a request without a token.

- Health check: https://siteledger-hkallem-api-btf6anh0erc4c2g4.canadacentral-01.azurewebsites.net/health/ready
- The API itself needs a token signed with the production key, so it isn't open for public use.
- Settings (connection string, signing key) live in App Service configuration, never in the repository.

## Results

Measured on 2026-11-02:

- **134 automated tests** (75 unit, 59 integration against SQL Server) run in [CI](https://github.com/harshakallem22/siteledger/actions) on every push.
- **99.6 % line coverage** of the service layer (`SiteLedger.Application.Services`).
- **Load test** (k6, 50 concurrent users, 30 s, median of 3 runs, 0 % errors, on a 4-vCPU GitHub Actions runner with the 10,000-row dataset):

| Endpoint | P95 latency without reporting indexes | P95 latency with them |
|---|---|---|
| Project budget-vs-actual | 93.4 ms | **46.6 ms** |
| Portfolio summary (sums all 10,000 rows) | 298.2 ms | **203.2 ms** |

The benchmark can be re-run from the **Benchmark** workflow in the Actions tab.

## Design choices

- **Money is `decimal`**, stored as `decimal(18,2)`; labor cost is rounded once at submission and stored.
- **No double approvals:** status change and audit row are saved in one transaction, protected by a `rowversion` concurrency check and a unique index on the audit table. A losing request gets `409 Conflict`.
- **Reports run in SQL** (`SUM … GROUP BY`): 3 queries for a project report, 2 for the portfolio, no per-project loops.
- **Uniqueness is enforced by the database** (project codes, one timesheet per employee per project per day), so concurrent requests can't slip past it.

## Limitations

- Demo authentication: tokens come from a local tool, not an identity provider. Roles are global; there are no per-project permissions.
- Not an accounting system: no invoicing, payroll, tax or multi-currency.
- The deployment uses free tiers: the app sleeps when idle and the database pauses, so the first request can take several seconds. Load tests were run in CI, not against Azure.
