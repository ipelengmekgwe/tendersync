# Running TenderSync locally with Docker Compose

Everything in `deploy/docker` (PLAN.md §15). Two ways to work:

| | What runs in Docker | What runs on your machine |
|---|---|---|
| **Full stack** | PostgreSQL, Seq, migrate, API, Worker, dashboard | nothing |
| **Infra only** | PostgreSQL, Seq | `dotnet run` (API, Worker) and `npm run dev` (dashboard) |

Both use the same Compose project (`tendersync`), so they share one database volume. Don't run the full stack and
`dotnet run`/`npm run dev` at the same time: both want ports 5000, 5001 and 3000.

## Full stack

PowerShell:

```powershell
./deploy/docker/tendersync.ps1 up
```

bash:

```bash
./deploy/docker/tendersync.sh up
```

The first `up` writes `deploy/docker/.env` with random secrets (JWT signing key, first-admin password, document master
key) and prints the admin password once. It then builds the images, starts everything and waits until
`/health/ready` and the login page answer.

| Service | URL |
|---|---|
| Dashboard | http://localhost:3000 |
| API (Scalar) | http://localhost:5000/scalar |
| Hangfire | http://localhost:5001/hangfire (open once as `/hangfire?access_token=<admin JWT>`) |
| Seq | http://localhost:5341 |
| PostgreSQL | `localhost:5432`, database `tendersync` |

`docker compose up -d --build`, run from `deploy/docker`, does the same without the waiting. From a clean clone it
works without a `.env`: the override file supplies public placeholder secrets.

### What happens on start

1. `postgres` and `seq` start; `postgres` reports healthy.
2. `migrate` (the Worker image with `--migrate-seed`) applies EF migrations, seeds categories, municipalities, roles
   and the first admin, then exits. `api` and `worker` wait for it to finish successfully.
3. `api` and `worker` start. The Worker registers its recurring jobs and runs `ingest-recent-releases` once straight
   away (`WORKER_TRIGGER_ON_START`; empty waits for the half-hour schedule). New tenders are classified as they arrive.
4. `web` starts once the API is healthy and talks to it on the internal network (`http://api:8080`).

Bid packs and encrypted company documents live in the `documents` volume (`/data/documents` in api and worker).

### Development and production settings

Run from `deploy/docker` (or through the scripts), Compose also loads `docker-compose.override.yml`:

- `ASPNETCORE_ENVIRONMENT=Development`: any email and password sign in as Admin (DevAuth, as with `dotnet run`), and
  `seed-demo` works.
- Placeholder secrets are used where `.env` leaves a value empty. They are public; never use the override on a server.

For the production-like stack, name the base file only. Real sign-in applies, and the API refuses to start without
`Jwt__SigningKey`:

```powershell
./deploy/docker/tendersync.ps1 up -Prod
```

```bash
docker compose -f deploy/docker/docker-compose.yml up -d --build
```

Ports bind to `127.0.0.1` (`BIND_ADDRESS`). Behind a TLS proxy, set `PUBLIC_WEB_URL` to the public address and
`AUTH_COOKIE_SECURE=true`.

### Keys and providers

Every line of `deploy/docker/.env` is passed to the api, worker and migrate containers, so any setting can go there
under its own name. For example:

```
Llm__Anthropic__ApiKey=sk-ant-...
Notifications__Telegram__BotToken=...
```

Back up `TENDERSYNC_MASTER_KEY`: encrypted company documents cannot be read without it. Rotation is in
`key-rotation.md`. `init` refuses to overwrite an existing `.env` unless you pass `-Force`.

Without an Anthropic key, the keyword stage still runs. The model stage fails and Hangfire retries it, which shows as
failed `ClassifyTenderJob` runs. Add the key, or route `classify` elsewhere with `Llm__Routing__classify`.

## Infra only

```powershell
./deploy/docker/tendersync.ps1 infra
```

```bash
docker compose -f deploy/docker/docker-compose.yml up -d postgres seq
```

Then run the hosts as in the README: `dotnet run --project src/TenderSync.Api`, `dotnet run --project
src/TenderSync.Worker` and `cd web && npm run dev`. Their secrets come from `dotnet user-secrets`, not
`deploy/docker/.env`.

## Everyday commands

| Task | PowerShell (`deploy/docker/tendersync.ps1 ...`) | bash (`deploy/docker/tendersync.sh ...`) |
|---|---|---|
| Containers and URLs | `status` | `status` |
| Follow logs | `logs` or `logs worker` | `logs worker` |
| Ingest the last n days now | `ingest 7` | `ingest 7` |
| Demo tenders for the e2e tests | `seed-demo` | `seed-demo` |
| Stop (data stays) | `down` | `down` |
| Delete database, documents and logs | `reset` (asks first) | `reset` |

`ingest` and `seed-demo` run in a throwaway Worker container (`docker compose run --rm worker ...`); the long-running
Worker keeps going.

## Troubleshooting

- **A port is already in use.** Stop `dotnet run`/`npm run dev`, or move the stack in `.env` (`API_PORT`,
  `WORKER_PORT`, `WEB_PORT`, `POSTGRES_PORT`, `SEQ_PORT`).
- **`migrate` exited with an error.** Run `docker compose logs migrate`. The usual cause is a database left from
  another branch's migrations.
- **Containers keep restarting with "SigningKey field is required".** The production-like stack has no `.env`; run
  `init`.
- **The postgres container is recreated after updating.** Compose recreated it because its settings changed, for
  example ports now bind to 127.0.0.1. The data is in the `tendersync_postgres-data` volume and is kept.
- **Running two copies side by side.** Use another project name and other ports and container names, as the WP15
  verification did:
  `docker compose -p tsverify` with `POSTGRES_CONTAINER_NAME`, `SEQ_CONTAINER_NAME` and the `*_PORT` variables set.
