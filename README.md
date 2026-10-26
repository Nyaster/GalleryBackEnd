# Gallery backend

See [API_REFERENCE.md](API_REFERENCE.md) for the frontend integration contract.

The API targets .NET 10, PostgreSQL with the `vector` extension, and a persistent local image volume. It applies its
initial EF migration at startup and creates an administrator only when both `BootstrapAdmin__Login` and
`BootstrapAdmin__Password` are configured.

## Run locally

Copy `.env.example` to `.env`, replace every placeholder, then run:

```bash
docker compose up --build
```

The API is exposed on `http://localhost:8080`; PostgreSQL and uploaded files persist in named volumes. The local compose
setup disables ONNX embeddings and marks the refresh cookie non-Secure solely because it uses HTTP. Enable TLS and set
`JwtConfig__RefreshCookieSecure=true` in every deployed environment. To enable embeddings, mount the model at
`/app/Data/model/model.onnx` and set `Embedding__Enabled=true`.

`ADMIN_LOGIN` and `ADMIN_PASSWORD` create the initial administrator when the database is first started. To enable
scraping, set `SCRAPER_ENABLED=true`, `SCRAPER_LOGIN`, and `SCRAPER_PASSWORD` in `.env`; these credentials are used only
to sign in to the configured source site. Restart the API after changing them.

For local SDK development, set `ConnectionStrings__DefaultConnection`, `JwtConfig__ValidIssuer`,
`JwtConfig__ValidAudience`, and `JwtConfig__SecretKey` in `secrets.json` or environment variables. `secrets.json` can
also contain `BootstrapAdmin` and `ParserSettings` sections; keep `ParserSettings:Enabled` false until valid source-site
credentials are provided. Then run:

```bash
dotnet restore GallerySiteBackend.sln
dotnet build GallerySiteBackend.sln
dotnet test GallerySiteUnitTests/GallerySiteUnitTests.csproj
dotnet run --project GallerySiteBackend/GallerySiteBackend.csproj
```

## API behavior

Gallery search, image details/files, recommendations, comments, tag suggestions, and daily discovery support guest
browsing. Guests see only approved, public, undeleted user uploads with an approved `sfw` tag. Attached `nsfw` tags
(including pending tags), and pending tag replacements proposing `nsfw`, block guest access. Signed-in users retain
existing broader access. Writes, personal feeds, rankings, announcements, and administration require authentication.
See [API_REFERENCE.md](API_REFERENCE.md) for the exact public routes and visibility rules.

Guest reads have no viewing quota and share a default limit of 300 requests per IP per minute per API process, including
image files. Configure `GuestBrowsing__RequestsPerMinute` for SDK runs or `GUEST_BROWSING_REQUESTS_PER_MINUTE` in the
Compose `.env` file. Guest responses are not cached; opened read routes vary by `Authorization`. Invalid/expired/revoked
tokens return `401`, while ineligible guest image URLs return `404`. No new migration or dependency is required.

`POST /api/auth/login` and `POST /api/auth/register` return a
short-lived access token and issue a rotating, Secure/HttpOnly refresh cookie. `POST /api/auth/refresh` reads that
cookie; refresh tokens are never returned in JSON.

Forgotten-password recovery requires an authenticator code or a saved one-time backup code. Normal sign-in remains
password-only. Users receive 10 backup codes on authenticator confirmation; backup-code password recovery unlinks the
lost app and requires fresh enrollment.
Non-administrators can upload after confirming an authenticator or receiving manual approval, unless explicitly blocked.
Compose persists authenticator encryption keys in the separate `gallery-auth-keys` volume; keep it when recreating
containers. Back up the complete key ring alongside the database. All API replicas must share that key ring and the
Data Protection application name `Lilgallery`. Losing the keys prevents app-code verification; saved backup codes
still work. For SDK development, keys default to `Data/auth-keys` under the application content root; override with
`Authenticator__KeyRingPath` when needed.

Apply the `AddAuthenticatorRecovery` and `AddAuthenticatorBackupCodes` migrations before starting the updated API.
Replace all old API instances together so session revocation and upload blocks are enforced consistently.

Authenticator management uses `POST /api/auth/authenticator/setup`, `/confirm`, `/remove`, and `/backup-codes`.
Setup returns a manual key, an authenticator URI, a setup ID, and a ten-minute expiry. Render QR codes locally.
Confirmation and backup-code regeneration return ten codes once; store them separately from the authenticator app.
Replacement, removal, and regeneration require the current password plus an app code or backup code. Starting a
replacement with the last backup code returns `409` without consuming it: remove the lost app first, then enroll again.
Anonymous `POST /api/auth/recover` accepts `login`, `code`, and `newPassword` and invalidates existing account sessions.
`GET /api/auth/me` returns current enrollment, remaining backup-code count, and effective upload access.
There is no email or administrator password-reset fallback.

Run `dotnet test GallerySiteBackend.sln` for unit and integration tests. Integration tests require Docker and use
isolated PostgreSQL containers.

Apply the `AddDiscoveryPeriodIndexes` migration when deploying the discovery query optimization. It adds date-first
indexes for daily likes and undeleted comments. Before recreating the API, run the Compose `migrate` service following
[deploy/DEPLOYMENT.md](deploy/DEPLOYMENT.md).

User uploads begin as `Pending`; only an administrator can approve or reject them. Gallery search and recommendations
include only approved, non-private images. The uploader and administrators can still read pending or private images
directly. API errors are RFC 7807 problem-details responses.

Upload and scrape imports share an image staging lifecycle: temporary data is inspected and moved to its final
storage key, but the staging handle still owns the file until the caller marks it persisted immediately after a
successful database save. Later response reads or source-stream disposal cannot delete a persisted file. Earlier
failures trigger best-effort cleanup even after request cancellation; deletion failures are logged without replacing
the original error. Database saving and file storage are not atomic, so process crashes or failed cleanup may leave
artifacts that require reconciliation.

## One-time scrape

After configuring scraper credentials and starting the API, log in with the bootstrap admin and copy `accessToken` from
the response. Queue a small scrape with `Incremental` mode:

```bash
curl -X POST http://localhost:8080/api/admin/scrape-runs \
  -H 'Authorization: Bearer <adminAccessToken>' \
  -H 'Content-Type: application/json' \
  -d '{"mode":"Incremental","maxImages":25}'
```

The response is `202 Accepted` and contains the run `id`. Check its progress until it is `Completed`:

```bash
curl http://localhost:8080/api/admin/scrape-runs/<runId> \
  -H 'Authorization: Bearer <adminAccessToken>'
```

`maxImages` is optional: it defaults to `ParserSettings:DefaultImagesPerRun` (100) and accepts values from 1 through
`ParserSettings:MaximumImagesPerRun` (2,000 by default). In Docker, set `SCRAPER_MAX_IMAGES_PER_RUN` in `.env` and
restart the API to change that ceiling. The run status reports live page and download counters while it is running.
`Incremental` fetches at most `ParserSettings:IncrementalPages` pages (five by default); `Full` scans every source page
before applying the download cap.

### Preview RAM cache

Preview JPEGs use a lazy, per-process RAM cache in front of the persistent disk cache.
`ImageStorage:PreviewMemoryCacheMegabytes` defaults to 128 MiB; set it to `0` to disable
RAM caching, or use the environment variable `ImageStorage__PreviewMemoryCacheMegabytes`
(range 0–1024). Restart the API after changing the setting. Original files and full-size
JPEG derivatives remain on the existing storage path.

Only nonempty previews up to and including 1 MiB are admitted. Entries expire after
one hour and may be evicted earlier to stay within the encoded-byte budget. Each API
replica warms its own cache on demand. The budget limits retained preview payloads,
not total process memory: active responses, admission copies (at most two additional
1 MiB buffers), conversion buffers, object overhead, and garbage collection add to RSS.
Authorization and original-file existence checks still run on every request.

With Podman Compose or Docker Compose, set `PREVIEW_MEMORY_CACHE_MEGABYTES=512` in
the Compose environment file to use a 512 MiB cache (default 128; `0` disables it).
The Compose files forward it to `ImageStorage__PreviewMemoryCacheMegabytes`.
Recreate the API container after changing its environment; for the production Compose file:

```bash
podman compose --env-file deploy/.env -f deploy/docker-compose.production.yml up -d --no-deps --force-recreate api
```
