# Azure deployment

TenderSync can run on Azure Container Apps (PLAN.md §15, WP17). The Bicep templates live in `deploy/azure`, and
`.github/workflows/deploy-azure.yml` deploys them. There are two environments, `dev` and `prod`, and each lives in its
own resource group.

## What gets deployed

| Resource | Name (dev) | Notes |
|---|---|---|
| User-assigned managed identity | `id-tendersync-dev` | Every app runs as it: registry pulls, Key Vault references, master key, blobs |
| Log Analytics + Application Insights | `log-` / `appi-tendersync-dev` | Container console logs; API and Worker requests, dependencies and Serilog events (ADR-0008). Daily cap 1 GB in dev |
| Key Vault (RBAC) | `kv-tsync-dev-<suffix>` | Master key `tendersync-master` (RSA 3072, wrap/unwrap only) and all secrets |
| Container registry (Basic) | `crtsyncdev<suffix>` | Images are imported from GHCR per deploy; admin user off |
| Storage account | `sttsyncdev<suffix>` | Private `documents` container, shared keys off, 14-day soft delete |
| PostgreSQL Flexible Server 17 | `psql-tendersync-dev-<suffix>` | Burstable B1ms, 32 GB, 7-day backups (14 in prod), TLS with certificate checks |
| Container Apps environment | `cae-tendersync-dev` | Consumption plan |
| `ca-tendersync-dev-web` | | Public HTTPS; the only public endpoint |
| `ca-tendersync-dev-api` | | Internal ingress; only the dashboard's server calls it |
| `ca-tendersync-dev-worker` | | Exactly one replica, no ingress (Hangfire dashboard optional) |
| `caj-tendersync-dev-migrate` | | Manual job: migrations and reference data, run before every rollout |

The `<suffix>` is derived from the resource group, so names stay the same between deployments.

### How configuration reaches the apps

- **Secrets** live only in Key Vault. The container apps hold Key Vault references and resolve them with the identity.
- **Generated secrets:** the workflow creates these once and never prints them.
  - `jwt-signing-key`
  - `postgres-admin-password`
  - `seed-admin-password`
- **Connection string:** Bicep writes `postgres-connection-string` from the admin password.
- **Optional secrets** are wired in only when they exist in the vault. A missing secret leaves that provider or channel
  unconfigured instead of breaking the deployment.

  | Key Vault secret | Setting |
  |---|---|
  | `anthropic-api-key` | `Llm__Anthropic__ApiKey` |
  | `openai-compatible-api-key` | `Llm__OpenAiCompatible__ApiKey` |
  | `smtp-password` | `Notifications__Email__SmtpPassword` |
  | `telegram-bot-token` | `Notifications__Telegram__BotToken` |
  | `teams-webhook-url` | `Notifications__Teams__WebhookUrl` |
  | `sms-api-key` | `Notifications__Sms__ApiKey` |

- **Plain settings** go in `appSettings` in `deploy/azure/<env>.bicepparam`, under their `Section__Key` names. Examples
  are the SMTP host, the from address and LLM routing. Never put a secret there.
- **Fixed by the templates:**
  - Production environment.
  - The Key Vault master key (`Encryption__Provider=KeyVault`).
  - Blob storage through managed identity (`DocumentStore__AzureBlob__ServiceUri`).
  - Application Insights.
  - The dashboard URL used in notifications and as the API's CORS origin.

## One-time setup

Run these with the Azure CLI, signed in to the subscription you want. The commands are for bash; in PowerShell, replace
the `\` line breaks with a backtick or put each command on one line.

1. **Resource group and providers.** South Africa North keeps the data in the country (POPIA) and close to eTenders.

   ```bash
   az group create --name rg-tendersync-dev --location southafricanorth
   ```

   ```bash
   for ns in Microsoft.App Microsoft.OperationalInsights Microsoft.Insights Microsoft.KeyVault Microsoft.ContainerRegistry Microsoft.Storage Microsoft.DBforPostgreSQL Microsoft.ManagedIdentity; do az provider register --namespace $ns; done
   ```

2. **A deployer identity that GitHub signs in as.** This uses OIDC, so no client secret exists. The identity needs
   Contributor and the right to create role assignments, because the templates grant the app identity its roles.

   ```bash
   az identity create --resource-group rg-tendersync-dev --name id-tendersync-dev-deployer
   ```

   ```bash
   az identity federated-credential create --resource-group rg-tendersync-dev --identity-name id-tendersync-dev-deployer \
     --name github-dev --issuer https://token.actions.githubusercontent.com \
     --subject repo:ipelengmekgwe/tendersync:environment:dev --audiences api://AzureADTokenExchange
   ```

   ```bash
   principal=$(az identity show -g rg-tendersync-dev -n id-tendersync-dev-deployer --query principalId -o tsv)
   rg=$(az group show -n rg-tendersync-dev --query id -o tsv)
   az role assignment create --assignee-object-id $principal --assignee-principal-type ServicePrincipal --role Contributor --scope $rg
   az role assignment create --assignee-object-id $principal --assignee-principal-type ServicePrincipal --role "Role Based Access Control Administrator" --scope $rg
   ```

   For prod, repeat steps 1 and 2 with `prod` in the names and the subject `repo:ipelengmekgwe/tendersync:environment:prod`.

3. **GitHub environment.** In Settings → Environments, create `dev` with these variables. They are identifiers, not
   secrets.

   | Variable | Value |
   |---|---|
   | `AZURE_CLIENT_ID` | `az identity show -g rg-tendersync-dev -n id-tendersync-dev-deployer --query clientId -o tsv` |
   | `AZURE_DEPLOYER_PRINCIPAL_ID` | the same identity's `principalId` |
   | `AZURE_TENANT_ID` | `az account show --query tenantId -o tsv` |
   | `AZURE_SUBSCRIPTION_ID` | `az account show --query id -o tsv` |
   | `AZURE_RESOURCE_GROUP` | `rg-tendersync-dev` |
   | `AZURE_OPERATOR_PRINCIPAL_ID` | your own object id (`az ad signed-in-user show --query id -o tsv`), so you can read and set secrets |
   | `TENDERSYNC_SEED_ADMIN_EMAIL` | the first Admin's email |

   For `prod`, also add **Required reviewers** and a deployment tag rule `v*`. A pushed `v*` tag then waits for your
   approval before it deploys.

4. **Images.** The workflow imports `ghcr.io/<owner>/tendersync-*:<sha>`, which `docker.yml` publishes when the commit
   reaches main. Deploy only commits that are on main, or pass an older SHA that was.

## Deploying

Actions → **Deploy to Azure** → Run workflow, choose `dev`, and leave the image tag empty to deploy the commit you run it
on. The run does the following:

1. Deploys the shared resources (Bicep pass 1).
2. Creates any missing generated secrets.
3. Imports the four images.
4. Deploys PostgreSQL, the environment and the apps (pass 2). The apps stay on the image they are already running.
5. Runs the migrate job.
6. Moves api, worker and web to the new image, but only after the migrate job succeeds.
7. Runs the smoke check:
   - `/login` answers 200.
   - `/api/proxy/tenders` answers 401, which proves the dashboard reaches the API.
   - Every revision reports Healthy. Readiness probes call `/health/ready`, which includes the database.

The run summary shows the dashboard URL.

On the very first deploy no app exists yet, so the apps start on the new image right away. They become healthy as soon as
the migrate job finishes.

### After the first deploy

- **Sign in** with `TENDERSYNC_SEED_ADMIN_EMAIL` and the generated password, then change it on `/admin/users`.

  ```bash
  az keyvault secret show --vault-name <kv-name> --name seed-admin-password --query value -o tsv
  ```

- **Add provider keys and channel secrets** from a file, so they stay out of your shell history. Then run the workflow
  again to wire them in.

  ```bash
  az keyvault secret set --vault-name <kv-name> --name anthropic-api-key --file ./anthropic.key --encoding utf-8 --output none
  ```

- **Set the company-document master key.** Nothing to do: the key is created by the templates. Rotation is in
  `docs/runbooks/key-rotation.md` (Key Vault provider).

## Secrets and logs

- **Values stay in Key Vault.**
  - Generated secret values go straight to Key Vault.
  - The PostgreSQL password is read into a variable that is masked with `::add-mask::` before anything else runs.
  - Nothing is echoed. The Azure CLI calls that touch secrets use `--output none` or `--query` on names only.
- **The PostgreSQL password** is a `@secure()` parameter that reaches Bicep through an environment variable. ARM does not
  keep secure parameter values in deployment history, and the templates never output them.
- **The containers** receive secrets as Key Vault references, so the Container Apps resources hold no secret values.
- **Registry access:** the GHCR token is the workflow's own `GITHUB_TOKEN`, which GitHub masks.

## Cost notes

These are estimates for South Africa North at the smallest sizes, at about R18 to the dollar. Check them in the
[Azure pricing calculator](https://azure.microsoft.com/pricing/calculator/) before relying on them.

| Item | dev | prod |
|---|---|---|
| Container Apps: worker (0.5 vCPU, 1 GiB, always on) | R150–600 (cheaper while idle) | same |
| Container Apps: api, web | ~R0–50, scale to zero | R250–700, one warm replica each |
| PostgreSQL B1ms + 32 GB storage | R300–400 | R300–400, plus backup storage beyond 32 GB |
| Container registry Basic | ~R90 | ~R90 |
| Log Analytics / Application Insights | R0–100 (first 5 GB a month free, 1 GB/day cap) | R0–200 |
| Key Vault, Storage | < R30 | < R50 |
| **Total a month** | **about R600–1 200** | **about R1 000–1 600** |

Ways to spend less:

- **Pause dev** when you are not using it:
  - Stop the database with `az postgres flexible-server stop`. Azure starts it again after 7 days.
  - Stop the worker with `az containerapp revision deactivate`, and start it again with `az containerapp revision activate`.
- **Remove dev** by deleting its resource group. Dev has no purge protection, so a soft-deleted vault can be purged
  (`az keyvault purge`).
- **Budget alert:** add one on the resource group in Cost Management, for example R1 500 a month.
- **Model spend** is separate from Azure. `Llm:DailyCostCapUsd` caps it, and `/admin/pipeline` shows it.

## Rollback

- **App code:** run the workflow again with `image_tag` set to the previous good SHA. The images are still in the
  registry. The migrate job on the older image finds no pending migrations, and the apps move back.
  - This works because migrations are additive by convention.
  - A release whose migration drops or renames something needs a forward fix instead. Note that in its pull request.
- **Faster, for one app:** point it at the previous image directly.

  ```bash
  az containerapp update -g rg-tendersync-dev -n ca-tendersync-dev-api --image <registry>/tendersync-api:<previous-sha>
  ```

  The next workflow run redeploys whatever tag the API is running.
- **Database:**
  1. Use point-in-time restore (7 days back in dev, 14 in prod) to a new server:
     `az postgres flexible-server restore --source-server <server> --name <new> --restore-time <UTC>`.
  2. Check the restored server, then update the `postgres-connection-string` secret to the new host.
  3. Restart the apps with `az containerapp revision restart`.

  Until you restore back under the original server name, do not run the workflow, because it rewrites that secret.
- **Secrets and keys:** Key Vault soft delete keeps deleted secrets and keys for 90 days (`az keyvault secret recover`,
  `az keyvault key recover`). Prod also has purge protection. Losing the master key makes every company document
  unreadable.
- **Documents:** blob soft delete keeps deleted and overwritten blobs for 14 days.

## Troubleshooting

- **Logs.** Use `az containerapp logs show -g <rg> -n <app> --follow`. For the migrate job, query Log Analytics:
  `ContainerAppConsoleLogs_CL | where ContainerJobName_s == "caj-tendersync-dev-migrate"`. Application Insights
  (Transaction search) has requests, outgoing calls and the structured Serilog events.
- **A revision fails right after the first deploy** with a Key Vault or registry access error. Role assignments can take
  a few minutes to apply. Run the workflow again.
- **Database connection fails with a certificate error.** The connection string uses `SSL Mode=VerifyFull` against the
  image's CA bundle. Check that the image still ships `ca-certificates`. As a stopgap, set `SSL Mode=Require` in
  `deploy/azure/modules/postgres.bicep`; it encrypts without checking the certificate.
- **Too many connections.** B1ms allows about 50. The connection string caps each Npgsql pool at 10, and API plus Worker
  hold up to four pools. Move to a larger SKU (`postgresSkuName`) before you raise the cap.
- **Hangfire dashboard.** Set `exposeWorkerDashboard = true` in the parameter file and deploy. The run summary's
  `workerDashboardUrl` output then serves `/hangfire`, with the same Admin-only access as locally. Turn it off again when
  you are done.

## Hardening later

The first version keeps to the consumption plan with public endpoints and no network isolation. Access is protected by
TLS, passwords, managed identity and Key Vault RBAC. The next steps:

- A VNet-integrated environment with private endpoints for PostgreSQL, Key Vault and Storage.
- Microsoft Entra authentication for PostgreSQL instead of the admin password.
- Front Door with a custom domain and a WAF.
