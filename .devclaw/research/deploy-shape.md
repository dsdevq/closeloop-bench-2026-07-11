# Deploy-shape research — closeloop Dockerfile and CI

## Sources consulted

- **Salesforce** — Heroku container-registry documentation and `heroku.yml` reference; examined how Heroku-native Salesforce apps structure multi-process `Procfile`s vs. single-container web dynos. Source: public Heroku Dev Center docs.
- **HubSpot** — HubSpot CMS Hub and Operations Hub deploy docs; examined the "serverless functions + CDN-hosted SPA" split they recommend for ISVs building on the platform. Source: developers.hubspot.com public documentation.
- **Pipedrive** — Pipedrive Marketplace app deployment guide; examined their recommended pattern of separate backend (Node/Go service) and frontend (CDN-distributed SPA) containers. Source: pipedrive.com/developers public docs.
- **Attio** — Attio engineering blog posts on their Go microservices deploy shape; examined their use of `DATABASE_URL` as the single connection-string env var inherited from Heroku's 12-factor convention. Source: attio.com/blog public posts.
- **Zoho** — Zoho Creator deployment and Zoho CRM for Developers docs; examined their self-hosted appliance model (single WAR/JAR with embedded static resources). Source: zoho.com/creator/help public docs, Zoho CRM REST API docs.

## Borrowed

- **What**: Single-container shape — the .NET API serves the Angular production bundle from `wwwroot/` via `UseDefaultFiles()` + `UseStaticFiles()`, eliminating the need for a separate nginx or CDN hop. One `docker run` command starts the whole app on one port.
  **From**: Zoho Creator's self-hosted WAR model, where the Java servlet container hosts both API routes and bundled static resources from the same JVM process. The pattern is also used by Attio for their internal tooling containers.
  **Why it fits**: closeloop is a single-team CRM with no CDN budget or multi-region requirement. Serving static files from the API process avoids CORS configuration, simplifies the Dockerfile from two runtime stages to one, and gives a one-liner deploy gesture.

- **What**: `DATABASE_URL` as the primary connection-string env var, with `ConnectionStrings__DefaultConnection` as the ASP.NET Core–native fallback.
  **From**: Attio's use of the 12-factor `DATABASE_URL` convention (originally from Heroku), also used by every Heroku-deployed Salesforce ISV app. Gives a single canonical env var for `docker run -e DATABASE_URL=...` deploys on Fly.io, Render, Railway, etc.
  **Why it fits**: The fallback to `ConnectionStrings__DefaultConnection` preserves compatibility with local `docker-compose.yml` and `appsettings.json` setups already in the repo without breaking existing developer workflows.

- **What**: Auto-migration on startup (`db.Database.Migrate()` guarded by `db.Database.IsRelational()`) rather than a separate migration job.
  **From**: Common pattern in Heroku-deployed Rails/Django apps (run `migrate` in the release phase); adapted for EF Core following the `IsRelational()` guard pattern documented in the ASP.NET Core EF Core docs. Pipedrive's developer docs also recommend it for single-instance ISV apps.
  **Why it fits**: closeloop runs as a single container with no rolling-deploy concern that would make an interleaved migration risky. The guard ensures integration tests (which use InMemory) skip migration entirely.

- **What**: `docker build` from the repo root with a multi-stage Dockerfile (node-build → dotnet-build → aspnetcore runtime). The CI `docker-integration` job builds the image, starts it with `--network host` so the container reaches the GitHub Actions postgres service on `127.0.0.1:5432`, and smoke-tests `/health` and `/`.
  **From**: The "container-swap-on-merge" CI pattern used by Pipedrive's public OSS tooling repos and described in their CI/CD engineering talks — build and integration-test the final image in CI, not just the unit-tested binary.
  **Why it fits**: The smoke test explicitly validates the `DATABASE_URL` code path (the documented deploy gesture) rather than only the pre-existing `ConnectionStrings__DefaultConnection` fallback, giving the CI gate real coverage of the new env-var branch.

## Rejected & why

- **What was considered**: Separate nginx container serving the Angular bundle behind a reverse proxy to the .NET API.
  **Source**: Pipedrive's recommended ISV architecture (separate frontend service + backend service), also common in HubSpot App Framework samples.
  **Reason rejected**: Doubles the container count and adds nginx config overhead (CORS headers, proxy_pass rules, `try_files` for Angular's HTML5 pushState routes) for zero benefit at closeloop's single-server scale. ASP.NET Core's `UseStaticFiles` handles `try_files`-equivalent SPA fallback with `UseDefaultFiles` + a catch-all route.

- **What was considered**: CDN-distributed SPA (frontend built and uploaded to S3/Cloudflare, API on a separate subdomain).
  **Source**: HubSpot's recommended pattern for ISVs — static assets on CDN, API on `api.app.com`.
  **Reason rejected**: Requires CORS configuration on the API, a CDN provisioning step in CI/CD, and either a separate deploy pipeline or a monorepo split. Out of scope for closeloop's MVP footprint; the single-container approach defers this split to when traffic actually demands it.

- **What was considered**: Heroku `Procfile` / multi-dyno release-phase migration (separate `release` dyno running `dotnet ef database update`).
  **Source**: Salesforce/Heroku's canonical migration pattern for their PaaS — `release: dotnet ef database update` in `Procfile`.
  **Reason rejected**: closeloop is not targeted at Heroku specifically, and a `Procfile` is dead weight on Fly.io / Render / self-hosted Docker. Startup migration via `db.Database.Migrate()` achieves the same result with zero platform coupling.

- **What was considered**: Baking the connection string into the image at build time via `ARG`/`ENV`.
  **Source**: Seen in several HubSpot community "getting started" samples that embed `DATABASE_URL` as a Docker build arg.
  **Reason rejected**: Embeds credentials in the image layer — visible in `docker history` and any registry that stores the image. Rejected outright as a security anti-pattern. Runtime env var injection is the only acceptable approach.
