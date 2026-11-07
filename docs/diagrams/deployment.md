# Deployment diagrams

TenderSync runs the same four images in both places (ADR-0017): `tendersync-api`, `tendersync-worker`,
`tendersync-migrate` and `tendersync-web`. `docker.yml` builds them and publishes them to GHCR on every merge to main.

## Docker Compose (local or a single VPS)

`deploy/docker/docker-compose.yml`; runbook `docs/runbooks/local.md`. Every port binds to `BIND_ADDRESS` (127.0.0.1 by
default), so nothing is reachable from other machines unless you change it or put a TLS proxy in front.

```mermaid
flowchart LR
  user([Browser])
  subgraph host["Docker host (127.0.0.1 ports)"]
    web["web<br/>Next.js :3000"]
    api["api<br/>ASP.NET Core :5000→8080"]
    worker["worker<br/>Hangfire servers :5001→8080<br/>/hangfire (Admin)"]
    migrate["migrate (one-shot)<br/>--migrate-seed"]
    pg[("postgres:17<br/>volume postgres-data")]
    seq["seq :5341<br/>log viewer"]
    docs[("volume documents<br/>/data/documents")]
  end
  etenders["eTenders OCDS API<br/>www.etenders.gov.za"]
  llm["Anthropic / OpenAI-compatible"]
  channels["SMTP, Telegram, Teams"]

  user -->|HTTP| web
  web -->|"/api/proxy → http://api:8080"| api
  migrate -->|"migrations + reference data, then exits"| pg
  api --> pg
  worker --> pg
  api --> docs
  worker --> docs
  api -.->|Serilog| seq
  worker -.->|Serilog| seq
  worker -->|"ingest, documents (throttled)"| etenders
  worker --> llm
  api -->|"reclassify on demand"| llm
  worker --> channels
```

The `api` and `worker` services start only after `migrate` has completed successfully. Secrets come from
`deploy/docker/.env`, which is git-ignored. `docker-compose.override.yml` supplies development placeholders.

## Azure (Container Apps)

`deploy/azure` (Bicep) and `.github/workflows/deploy-azure.yml`; runbook `docs/runbooks/azure.md`.

```mermaid
flowchart TB
  user([Browser])
  gh["GitHub Actions<br/>deploy-azure.yml (OIDC)"]
  ghcr[("GHCR")]
  ext["eTenders, model providers,<br/>SMTP / Telegram / Teams"]

  subgraph rg["Resource group rg-tendersync-{env}"]
    subgraph cae["Container Apps environment (all run as id-tendersync-{env})"]
      web["ca-…-web<br/>public HTTPS"]
      api["ca-…-api<br/>internal ingress"]
      worker["ca-…-worker<br/>1 replica"]
      job["caj-…-migrate<br/>manual job"]
    end
    acr[("Container registry")]
    kv["Key Vault<br/>master key + secrets"]
    st[("Storage<br/>documents container")]
    pg[("PostgreSQL 17<br/>B1ms")]
    mon["Log Analytics +<br/>App Insights"]
  end

  user -->|HTTPS| web
  web -->|"http://ca-…-api"| api
  api & worker & job --> pg
  api & worker --> st
  api & worker -->|"secrets, data keys"| kv
  cae -.->|"logs, telemetry"| mon
  acr -.->|"images"| cae
  worker --> ext
  ghcr -->|"az acr import"| acr
  gh -->|"Bicep, migrate, rollout"| cae
  gh -->|"generated secrets"| kv
```

Only the web app has a public endpoint. Every app and the job run as the managed identity `id-tendersync-{env}`, which
pulls images, resolves Key Vault references, wraps document keys and reads and writes blobs. The managed identity's roles:

| Role | Scope | Allows |
|---|---|---|
| AcrPull | Registry | Pulling images |
| Key Vault Secrets User | Vault | Resolving secret references |
| Key Vault Crypto User | Master key | Wrapping and unwrapping document data keys |
| Storage Blob Data Contributor | Storage account | Reading and writing documents |

The deployer identity has Contributor and Role Based Access Control Administrator on the resource group, plus Key Vault
Secrets Officer on the vault.

## Release flow

```mermaid
flowchart LR
  pr["Pull request"] -->|"ci.yml: build, tests, coverage, vulnerability scans, Bicep lint<br/>docker.yml: image builds"| main["main"]
  main -->|"docker.yml"| ghcr[("GHCR :sha, :latest")]
  ghcr -->|"deploy-azure.yml (manual or v* tag)"| dev["dev / prod"]
  dev -->|"migrate job succeeds"| roll["apps move to the new image"]
  roll --> smoke["smoke check: /login 200, /api/proxy/tenders 401, revisions Healthy"]
```
