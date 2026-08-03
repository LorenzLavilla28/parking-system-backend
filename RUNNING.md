# Running ParkingSaaS locally

This guide covers the complete local application on Windows and macOS. The supported
path uses Docker Compose for PostgreSQL and the API, and runs the Vite frontend on the
host. All Docker Compose commands are run from `parking-system-backend/` unless stated
otherwise.

Local URLs:

| Service | URL |
|---|---|
| Frontend | http://localhost:5173 |
| API | http://localhost:5274 |
| Swagger | http://localhost:5274/swagger |
| Health | http://localhost:5274/api/health |
| PostgreSQL | localhost:5432 |

## Recommended Docker Compose workflow

### Prerequisites for Windows

Install [Docker Desktop for Windows](https://www.docker.com/products/docker-desktop/),
[Git for Windows](https://git-scm.com/download/win), and
[Node.js 20 or newer](https://nodejs.org/). Start Docker Desktop before running the
commands below. Enable the WSL 2 backend if Docker Desktop asks.

### Prerequisites for macOS

Install [Docker Desktop for Mac](https://www.docker.com/products/docker-desktop/),
[Git](https://git-scm.com/download/mac) or Xcode Command Line Tools, and
[Node.js 20 or newer](https://nodejs.org/). Start Docker Desktop before running the
commands below. Intel and Apple Silicon Macs are supported.

Verify Docker Compose and Node:

```bash
docker --version
docker compose version
node --version
npm --version
```

Use `docker compose` with a space; the older standalone `docker-compose` command is
not required.

### Create the backend environment file

From the repository root, enter the backend directory:

```bash
cd parking-system-backend
```

Windows PowerShell:

```powershell
Copy-Item .env.example .env
```

macOS Terminal:

```bash
cp .env.example .env
```

The default `.env` values start PostgreSQL as `parkingsaas` with user `parking` and
password `parking`, expose the API on port `5274`, allow the frontend at port `5173`,
keep database reset disabled, and keep email disabled. The file is local-only and must
not be committed.

### Start PostgreSQL and the API

Run from `parking-system-backend/`:

```bash
docker compose -f deploy/docker-compose.yml config
docker compose -f deploy/docker-compose.yml up -d --build
docker compose -f deploy/docker-compose.yml ps
```

The API waits for the PostgreSQL health check and applies EF Core migrations on
startup. Follow logs when needed:

```bash
docker compose -f deploy/docker-compose.yml logs -f api
```

Open <http://localhost:5274/api/health> to verify the API. In Development, the
platform administrator is seeded with:

| Role | Email | Password |
|---|---|---|
| Platform administrator | `platform@parking.local` | `Platform!2026` |

### Start the frontend

Open a second terminal.

Windows PowerShell:

```powershell
cd parking-system-frontend
npm ci
npm run dev
```

macOS Terminal:

```bash
cd parking-system-frontend
npm ci
npm run dev
```

Open <http://localhost:5173>. The checked-in `frontend/.env.development` points to
`http://localhost:5274`. If you change `API_PORT` in the backend `.env`, update
`parking-system-frontend/.env.development` and restart Vite:

```text
VITE_API_BASE_URL=http://localhost:<api-port>
```

### Stop and restart

Stop the stack while keeping database data:

```bash
docker compose -f deploy/docker-compose.yml down
```

Start it again later:

```bash
docker compose -f deploy/docker-compose.yml up -d
```

Rebuild after backend or Dockerfile changes:

```bash
docker compose -f deploy/docker-compose.yml up -d --build api
```

Do not add `-v` unless you intentionally want to delete the local database volume.

## PayMongo and AWS Secrets Manager

PayMongo credentials are tenant-owned. There is no local or global PayMongo key
fallback. If a tenant has not connected its own PayMongo account, the public payment
page is cash-only and guards can record cash when cash is enabled for the location.

To connect a tenant’s live PayMongo account from the local app, the API container needs
AWS credentials. Add them only to the untracked `parking-system-backend/.env` file:

```dotenv
AWS_REGION=ap-southeast-1
AWS_ACCESS_KEY_ID=your-access-key-id
AWS_SECRET_ACCESS_KEY=your-secret-access-key
AWS_SESSION_TOKEN=
AWS_SECRETS_ENABLED=true
```

Use temporary credentials where possible. Never put AWS credentials in frontend
environment files, source code, Docker images, or Git. The IAM identity needs:

- `secretsmanager:CreateSecret`
- `secretsmanager:PutSecretValue`
- `secretsmanager:GetSecretValue`

The API container receives these values through Docker Compose. Confirm the region
without printing secret values:

```bash
docker compose -f deploy/docker-compose.yml exec api printenv AWS_REGION
```

Then sign in as the tenant administrator, open Payment settings, and connect that
tenant’s live credentials. Only server-side keys beginning with `sk_live_` are accepted;
test keys are rejected. Redirect URLs use `PUBLIC_BASE_URL` (default
`http://localhost:5173`).

PayMongo webhooks cannot normally reach localhost from the public internet. The payment
flow also polls PayMongo status, but a full webhook verification requires a public HTTPS
URL or a tunnel. Live credentials can create real charges, so do not initiate local
checkout payments unless that is intentional.

Credentials stored in the host `.aws` directory are not automatically visible inside a
Docker container. For Compose, use the untracked `.env` variables above, or create a
local Compose override that mounts the host credentials file read-only into
`/root/.aws/`. Keep that override out of Git.

## Email in Docker

Email is disabled by default in the Compose environment. Real delivery uses Microsoft
Graph only. Add the Entra app-only credentials to the backend `.env`, then recreate the
API container:

```dotenv
EMAIL_ENABLED=true
EMAIL_TENANT_ID=your-microsoft-entra-tenant-id
EMAIL_CLIENT_ID=your-application-client-id
EMAIL_CLIENT_SECRET=your-application-client-secret
EMAIL_FROM_ADDRESS=noreply@your-domain.example
EMAIL_FROM_NAME=PBP Parking
EMAIL_APP_BASE_URL=http://localhost:5173
```

The Entra application needs Microsoft Graph `Mail.Send` application permission with
admin consent. `EMAIL_FROM_ADDRESS` must identify a mailbox the application is allowed
to send as.

```bash
docker compose -f deploy/docker-compose.yml up -d --build api
```

## Native development prerequisites (optional)

- **.NET 10 SDK** — `dotnet --version` should print `10.x`.
- **PostgreSQL** listening on port 5432, with database `parkingsaas`, user `parking`,
  password `parking` (this is `ConnectionStrings:Default`). A local install is fine; a
  container works too, see [Running PostgreSQL in Docker](#running-postgresql-in-docker).

## Native backend quick start (optional)

```bash
dotnet run --project src/ParkingSaaS.Api --launch-profile http
```

The API listens on <http://localhost:5274>. Swagger UI is at
<http://localhost:5274/swagger>, health at <http://localhost:5274/api/health>. Use
`--launch-profile https` for <https://localhost:7134> as well.

**Pass the launch profile.** A bare `dotnet run --project src/ParkingSaaS.Api` skips
`launchSettings.json`, so `ASPNETCORE_ENVIRONMENT` is unset, the app boots as **Production**
on port 5000, and you get no Swagger, no seed data, and no user-secrets. Alternatively set
`ASPNETCORE_ENVIRONMENT=Development` in your shell.

On startup in Development the app applies EF migrations and seeds only the platform account. Because
`Database:ResetOnStartup` is `false` in `appsettings.Development.json`, **the database is
dropped and rebuilt on every boot** — expected locally, and it can never fire outside
Development (`Program.cs` gates it on the environment).

### Seeded logins

| Role | Email | Password |
|---|---|---|
| Platform administrator | `platform@parking.local` | `Platform!2026` |

## Native development secrets (optional)

Development secrets live in .NET user-secrets, not in the repo. They are keyed to the
`UserSecretsId` in `src/ParkingSaaS.Api/ParkingSaaS.Api.csproj` and stored under
`%APPDATA%\Microsoft\UserSecrets\` on Windows.

Outbound email uses Microsoft Graph app-only authentication. Development values are read
from the `Email` section in `appsettings.Development.json`; do not commit real client
secrets to source control. For a longer-lived local setup, store the secret with .NET
user-secrets:

```bash
cd src/ParkingSaaS.Api
dotnet user-secrets set "Email:ClientSecret" "<microsoft entra client secret>"
dotnet user-secrets list          # verify
```

Set `Email:TenantId`, `Email:ClientId`, and `Email:FromAddress` to the matching Entra
tenant, application, and mailbox. Microsoft Graph is the only supported delivery
transport.

To skip email entirely instead, set `Email:Enabled` to `false` in
`appsettings.Development.json`. Queued mail is then written to the log by
`LoggingEmailSender` rather than sent.

`Email:Enabled` is validated at startup: if the Graph configuration is incomplete, or
`Email:FromAddress` is empty, the app refuses to boot rather than silently dropping mail.

## Docker Compose reference

`deploy/docker-compose.yml` is a portable development stack for WSL, Linux, macOS, and
Windows Docker Desktop. It starts PostgreSQL and the API.

### Running from WSL (alternative)

Install Docker Desktop on the host, enable its WSL 2 integration for your distribution, then
run these commands from the repository root in WSL:

```bash
docker compose -f deploy/docker-compose.yml config
docker compose -f deploy/docker-compose.yml up --build
```

The backend `.env` file is optional for the database-only flow. PayMongo credentials are
not bundled and there is no global fallback; connect each tenant's own account through
the application using AWS Secrets Manager.

The API is available at <http://localhost:5274> and PostgreSQL at `localhost:5432`. If a
port is already in use, change the matching value in `.env`. Named volumes keep the database
and API Data Protection keys across container restarts.

Stop the stack with `Ctrl+C`, or run `docker compose -f deploy/docker-compose.yml down` from
another WSL session. Add `-v` only when you intentionally want to delete persisted data.

`deploy/docker-compose.yml` defines PostgreSQL and the API. It needs a running Docker daemon
and the Compose v2 plugin — a standalone `docker` CLI is not enough.

### Running PostgreSQL in Docker

Useful if you don't want PostgreSQL installed on the host. Start only that service and run
the API normally; this keeps the fast rebuild loop, the debugger, and user-secrets.

```bash
docker compose -f deploy/docker-compose.yml up -d postgres
dotnet run --project src/ParkingSaaS.Api --launch-profile http
```

### Running everything in Docker

```bash
docker compose -f deploy/docker-compose.yml up --build
```

The API is then on <http://localhost:5274>. User-secrets do not exist inside the container,
so Graph delivery stays off unless you put the credentials in `.env` first:

```bash
EMAIL_ENABLED=true
EMAIL_TENANT_ID='<microsoft entra tenant id>'
EMAIL_CLIENT_ID='<application client id>'
EMAIL_CLIENT_SECRET='<application client secret>'
EMAIL_FROM_ADDRESS='noreply@your-domain.example'
docker compose -f deploy/docker-compose.yml up --build
```

## Tests

```bash
dotnet test
```

Unit tests only (xUnit + FluentAssertions). No database or network required.

## Migrations

Migrations are applied automatically at startup, so this is only needed when changing the
model. `--startup-project` is required because the DbContext lives in Infrastructure while
the connection string is configured in the API host.

```bash
dotnet ef migrations add <Name> \
  --project src/ParkingSaaS.Infrastructure \
  --startup-project src/ParkingSaaS.Api

dotnet ef database update \
  --project src/ParkingSaaS.Infrastructure \
  --startup-project src/ParkingSaaS.Api
```

Install the tool once with `dotnet tool install --global dotnet-ef` if `dotnet ef` is not found.

## Troubleshooting

**`Npgsql.NpgsqlException: Connection refused`** — PostgreSQL is not up, or is not on 5432.
Check with `netstat -ano | findstr :5432` (Windows).

**`password authentication failed for user "parking"`** — the local PostgreSQL has no
`parking` role or `parkingsaas` database. Create them, or point
`ConnectionStrings:Default` in `appsettings.Development.json` at credentials you do have.

**`docker: unknown command: docker compose`** — you have the standalone Docker CLI without
the Compose v2 plugin, and probably no daemon. Install Docker Desktop, or just run
PostgreSQL on the host.

**Swagger 404, no seed users, port is 5000** — you ran without a launch profile, so the app
started in Production. The recommended Docker Compose flow uses Development automatically;
see [Recommended Docker Compose workflow](#recommended-docker-compose-workflow).

**`Email is enabled but its Microsoft Graph credentials are incomplete`** — a startup
validation failure from `EmailOptions`. Set `TenantId`, `ClientId`, and `ClientSecret`, or
set `Enabled` to `false`.

**Microsoft Graph returns 401/403** — verify the Entra tenant/application IDs, rotate an
expired client secret, grant the application-level `Mail.Send` permission with admin
consent, and confirm the application may send as `Email:FromAddress`.

**Data from a previous run is gone** — expected. See `Database:ResetOnStartup` above. Set it
to `false` in `appsettings.Development.json` to keep data across restarts.

## Related

- API reference: [`../docs/API.md`](../docs/API.md)
- Project overview: [`../README.md`](../README.md)
