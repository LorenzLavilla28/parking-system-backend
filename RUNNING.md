# Running the backend

How to get the ParkingSaaS API up on a development machine. All commands are run from
`parking-system-backend/` unless stated otherwise.

## Prerequisites

- **.NET 10 SDK** — `dotnet --version` should print `10.x`.
- **PostgreSQL** listening on port 5432, with database `parkingsaas`, user `parking`,
  password `parking` (this is `ConnectionStrings:Default`). A local install is fine; a
  container works too, see [Running PostgreSQL in Docker](#running-postgresql-in-docker).

## Quick start

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

On startup in Development the app applies EF migrations and seeds demo data. Because
`Database:ResetOnStartup` is `true` in `appsettings.Development.json`, **the database is
dropped and rebuilt on every boot** — expected locally, and it can never fire outside
Development (`Program.cs` gates it on the environment).

### Seeded logins

| Role | Email | Password |
|---|---|---|
| Platform administrator | `platform@parking.local` | `Platform!2026` |
| Tenant administrator | `admin@demo.local` | `Admin!2026` |
| Supervisor | `supervisor@demo.local` | `Super!2026` |
| Guard | `guard@demo.local` | `Guard!2026` |

## Secrets

Development secrets live in .NET user-secrets, not in the repo. They are keyed to the
`UserSecretsId` in `src/ParkingSaaS.Api/ParkingSaaS.Api.csproj` and stored under
`%APPDATA%\Microsoft\UserSecrets\` on Windows.

Outbound email is enabled in Development and needs an SMTP password. Without it the API
still starts, but every queued message fails to send and dead-letters after 5 attempts.

```bash
cd src/ParkingSaaS.Api
dotnet user-secrets set "Email:Password" "<gmail app password>"
dotnet user-secrets list          # verify
```

To skip email entirely instead, set `Email:Enabled` to `false` in
`appsettings.Development.json`. Queued mail is then written to the log by
`LoggingEmailSender` rather than sent.

`Email:Enabled` is validated at startup: if it is `true` while `Email:Host` or
`Email:FromAddress` is empty, the app refuses to boot rather than silently dropping mail.

## Docker

`deploy/docker-compose.yml` is a portable development stack for WSL, Linux, macOS, and
Windows Docker Desktop. It starts PostgreSQL, the API, and a MinIO S3-compatible service.

### Running from WSL

Install Docker Desktop on the host, enable its WSL 2 integration for your distribution, then
run these commands from the repository root in WSL:

```bash
cp .env.example .env
docker compose -f deploy/docker-compose.yml config
docker compose -f deploy/docker-compose.yml up --build
```

The API is available at <http://localhost:8080>, PostgreSQL at `localhost:5432`, and the
MinIO console at <http://localhost:9001>. If a port is already in use, change the matching
value in `.env`. Named volumes keep the database, MinIO data, and API Data Protection keys
across container restarts.

Stop the stack with `Ctrl+C`, or run `docker compose -f deploy/docker-compose.yml down` from
another WSL session. Add `-v` only when you intentionally want to delete persisted data.

`deploy/docker-compose.yml` defines PostgreSQL, the API, and a MinIO S3 emulator. It needs a
running Docker daemon and the Compose v2 plugin — a standalone `docker` CLI is not enough.

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

The API is then on <http://localhost:8080>. User-secrets do not exist inside the container,
so SMTP stays off unless you put the credentials in `.env` first:

```bash
EMAIL_ENABLED=true
EMAIL_HOST=smtp.gmail.com
EMAIL_USERNAME=you@example.com
EMAIL_PASSWORD='<gmail app password>'
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
started in Production. See [Quick start](#quick-start).

**`Email:Enabled is true but Email:Host is not set`** — a startup validation failure from
`EmailOptions`. Fill in the `Email` section or set `Enabled` to `false`.

**SMTP auth fails against Gmail** — the password must be a 16-character *app password*, not
the account password, and 2-Step Verification must be on. Port 587 with `UseSsl: true` means
STARTTLS. Port 465 will not work: `System.Net.Mail.SmtpClient` does not support implicit TLS.

**Data from a previous run is gone** — expected. See `Database:ResetOnStartup` above. Set it
to `false` in `appsettings.Development.json` to keep data across restarts.

## Related

- API reference: [`../docs/API.md`](../docs/API.md)
- Project overview: [`../README.md`](../README.md)
