# Gallery backend

See [API_REFERENCE.md](API_REFERENCE.md) for the frontend integration contract.

## Project structure

The solution has three production projects and separate unit and integration test projects:

| Project              | Responsibilities                                                                                  |
|----------------------|---------------------------------------------------------------------------------------------------|
| `GallerySiteBackend` | API host, controllers, middleware, HTTP user context, and background workers                      |
| `Application`        | Feature handlers, domain models, DTOs, repository/service abstractions, and shared configuration  |
| `Infrastructure`     | Persistence, EF Core migrations, authentication services, storage, image processing, and scraping |

The API references Application and Infrastructure; Infrastructure references Application.
Application has no production project dependencies. Existing namespaces are retained so the
consolidation preserves type names and logging categories. MediatR and repository behavior are unchanged.

EF Core migrations live in `Infrastructure/Persistence/Migrations`. For example, check model changes with:

```bash
dotnet ef migrations has-pending-model-changes --project Infrastructure --startup-project GallerySiteBackend
```

## Runtime

The API targets .NET 10, PostgreSQL with the `vector` extension, and a persistent local image volume. Its
`--migrate` command applies EF migrations and can create an administrator when both `BootstrapAdmin__Login` and
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
dotnet run --project GallerySiteBackend/GallerySiteBackend.csproj -- --migrate
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
Compose `.env` file. Guest responses are not cached; opened read routes vary by `Authorization`. Invalid/expired
tokens return `401`, while ineligible guest image URLs return `404`.

Apply the `AddContentVisibility` migration before deploying the content settings API. It starts existing and new
accounts with **Display all content** off and creates an empty rule list. Once all API instances are updated, admins
can add individual approved tags or groups of tags that must occur together. The backend applies the rules to guests
and to accounts with the switch off. The switch is saved per account and takes effect with the current access token.
See the account and admin endpoints in [API_REFERENCE.md](API_REFERENCE.md#display-all-content).

`POST /api/auth/login` and `POST /api/auth/register` return a
short-lived access token and issue a rotating, Secure/HttpOnly refresh cookie. `POST /api/auth/refresh` reads that
cookie; refresh tokens are never returned in JSON. Access-token validation checks the signature, issuer, audience,
expiry, and user-ID claim locally. Each authenticated request resolves current roles and permissions from PostgreSQL.
Role assignments and edits therefore take effect with an existing token. Existing access tokens remain valid after
password
recovery until expiry (`JwtConfig:AccessTokenMinutes`, default 15 minutes, plus 30 seconds of validation clock skew).
Account recovery immediately revokes refresh sessions, preventing those sessions from issuing new access tokens.

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
Replace all old API instances together so access-token validation, refresh-session revocation, and upload blocks are
enforced consistently.

Authenticator management uses `POST /api/auth/authenticator/setup`, `/confirm`, `/remove`, and `/backup-codes`.
Setup returns a manual key, an authenticator URI, a setup ID, and a ten-minute expiry. Render QR codes locally.
Confirmation and backup-code regeneration return ten codes once; store them separately from the authenticator app.
Replacement, removal, and regeneration require the current password plus an app code or backup code. Starting a
replacement with the last backup code returns `409` without consuming it: remove the lost app first, then enroll again.
Anonymous `POST /api/auth/recover` accepts `login`, `code`, and `newPassword` and revokes existing refresh sessions.
Already-issued access tokens expire naturally; they are not immediately revoked by recovery.
`GET /api/auth/me` returns current enrollment, remaining backup-code count, and effective upload access.
There is no email or administrator password-reset fallback.

Run `dotnet test GallerySiteBackend.sln` for unit and integration tests. Integration tests require Docker and use
isolated PostgreSQL containers.

Apply the `AddDiscoveryPeriodIndexes` migration when deploying the discovery query optimization. It adds date-first
indexes for daily likes and undeleted comments. Before recreating the API, run the Compose `migrate` service following
[deploy/DEPLOYMENT.md](deploy/DEPLOYMENT.md).

User uploads begin as `Pending` unless the uploader has `uploads.bypassReview`. Accounts with `images.moderate`
can approve or reject images. Gallery search and recommendations include only approved, non-private images.
Owners and accounts with `images.readRestricted` can read restricted images subject to the visibility settings.
API errors are RFC 7807 problem-details responses.

Admins can manage role definitions and account assignments in the staff access screen. The migration preserves
existing Admin and Moderator access and gives each account the baseline User role. Trusted Contributor starts
unassigned. Assigning it grants both automatic approval for eligible new uploads and immediate tag changes on
the contributor’s own images. Upload blocks, selected visibility, and global tag moderation still apply.

The role editor sends `expectedRoleIds` from the loaded account with its replacement `roleIds`. The API checks
that snapshot under the assignment write lock. A stale draft receives HTTP 409 with Problem Details type
`urn:lilgallery:problem:role-assignments-changed`. During rollout, older clients may omit the snapshot and
write unconditionally. Once all writers send it, set `RoleAssignments__RequireExpectedRoleIds=true`
(`REQUIRE_EXPECTED_ROLE_IDS=true` in Compose) on every API replica. An explicit JSON null is invalid in either mode.

For this release, back up PostgreSQL, stop the old API replicas, run the new image with `--migrate`, then
start the new API and serve the matching SPA. Check the role memberships before editing them. Once custom
roles or grants are in use, an application rollback requires a release that understands the new role tables.
See [the access management API](API_REFERENCE.md#roles-and-permissions).

Upload and scrape imports share an image staging lifecycle: temporary data is inspected and moved to its final
storage key, but the staging handle still owns the file until the caller marks it persisted immediately after a
successful database save. Later response reads or source-stream disposal cannot delete a persisted file. Earlier
failures trigger best-effort cleanup even after request cancellation; deletion failures are logged without replacing
the original error. Database saving and file storage are not atomic, so process crashes or failed cleanup may leave
artifacts that require reconciliation.

## Frontend handoff for role and moderation writes

Deploy the role editor, moderation queue, and image detail changes with the matching API.
Older role editors and status forms receive `400` when they omit a required baseline.

- **Edit role definitions.** Read the opaque `version` from `GET /api/admin/access/roles`. For
  `PUT /api/admin/access/roles/{id}`, send the draft’s original version as `expectedVersion`, alongside
  `name` and the complete `permissions` array. Successful creates and updates return the current version.
  On `409` with type `urn:lilgallery:problem:role-definition-changed`, reload the list and let the administrator
  review the latest grants before retrying. Member count changes alone keep the version valid. Role creation
  still accepts just `name` and `permissions`.
- **Assign account roles.** Send `roleIds` and the original `expectedRoleIds` to
  `PUT /api/admin/access/accounts/{id}/roles`. A stale snapshot returns the typed assignment conflict even if a
  desired role was removed and deleted. An unknown desired ID with a matching snapshot returns `400`.
- **Moderate or classify an image.** For `PATCH /api/admin/images/{id}/moderation`, a status decision takes
  `{"status":"Approved","expectedStatus":"Pending"}` when the reviewer saw a pending image. Classification alone
  takes, for example, `{"aiUsage":"HumanMade"}`. Show each control on non-hidden images with its corresponding
  permission. Handle `200` with a readable card and `204` with an empty body. Refresh ordinary detail or the
  queue after a save. On `409`, refresh before offering another decision. Bodyless
  `POST /api/images/{id}/publish` and `POST /api/images/{id}/restore` also use these response codes.
- **Decide a tag proposal.** For `PATCH /api/admin/tag-changes/{id}/moderation`, send `decision` as
  `Approve` or `Reject` for a pending replacement and `Revert` for an eligible approved replacement. Each
  accepts an optional `note`. On `409`, refresh the proposal or history. Global tag rejection entries are audit
  history.
- **Display tag history and queue previews.** Read `canViewTagHistory` independently of `canEditTags`. Owners
  and holders of `imageTags.review` may read image tag history. Keep the proposal and its decision controls
  usable when an ordinary image detail request returns `403` or `404` while loading the queue preview.

## Personalized recommendations

`GET /api/images/for-me` builds a signed-in user's feed from up to 25 representative images selected from their latest
100 eligible likes. Set `ForMeFeed__RepresentativeCount` for SDK/container configuration, or
`FOR_ME_REPRESENTATIVE_COUNT` in the Compose `.env` file, to choose a limit from 1 through 100. Restart the API after
changing it. More references cover more interests, but increase search work; the default latency budget is measured
at 25 references, not promised for a limit of 100.

Feed creation requires a released pgvector version **0.8.0 or newer** and uses the HNSW index with
`m=16, ef_construction=128`. Check the
installed `vector` extension version (`SELECT extversion FROM pg_extension WHERE extname = 'vector';`) before rollout;
the API does not upgrade it automatically. Index searches retrieve
at most 500 candidates per reference for both filtered and unfiltered feeds and order them by exact
distance, so they can miss some globally nearest matches. One exact top-up
can fill an undersized personalized result. Generation resolves only currently approved requested tag names within its
repeatable-read snapshot, skipping an
unnecessary tag join when none match. Original filter names remain stored, so later cursor pages still suppress
images when an unknown or pending tag becomes approved or attached. Cursor requests reuse the stored feed for one hour,
retain current
eligibility checks, and remain compatible across reference-limit changes. Apply the `ImproveForMeHnswConstruction`
migration before running the updated API. It rebuilds the existing index concurrently with higher construction effort.
See the ForMe section in [API_REFERENCE.md](API_REFERENCE.md) for the request contract.

Only unfiltered ANN retrieval with usable references temporarily sets transaction-local `random_page_cost=1.1` to
encourage use of the index. The original value is restored before exact top-up, popularity fallback,
or publication; filtered searches retain their planner costs and may choose an efficient exact plan. This does not
change global/session defaults or disable sequential scans. Search tuple/memory settings are approximate search budgets,
not strict process-memory limits. The benchmark's larger maintenance memory is confined to isolated index construction.

The migration changes index construction options and then runs `REINDEX INDEX CONCURRENTLY` as separate commands
outside a transaction. Ordinary reads/writes can continue; PostgreSQL briefly locks during index replacement.
Budget for an additional index copy, WAL, temporary disk, and the measured staging build time plus margin. There is no
automatic production maintenance-memory increase. Before rollout, check free disk and the server's `statement_timeout`.
`DatabaseMigrations:CommandTimeoutSeconds` defaults to 600 (positive integer), affects migration commands only, and is
restored before administrator bootstrap. Override with `DatabaseMigrations__CommandTimeoutSeconds` or Compose
`MIGRATION_COMMAND_TIMEOUT_SECONDS`. For example, after measuring staging, a finite one-hour command budget is:

```bash
docker compose -f deploy/docker-compose.production.yml run --rm -e DatabaseMigrations__CommandTimeoutSeconds=3600 migrate
```

This budget is an example, not a build-time guarantee. If interrupted, inspect `pg_index.indisvalid` and PostgreSQL's
`_ccnew`/`_ccold` replacement indexes before retrying;
follow [PostgreSQL concurrent-reindex recovery instructions](https://www.postgresql.org/docs/17/sql-reindex.html#SQL-REINDEX-CONCURRENTLY)
to
remove an invalid leftover or complete a pending old-index drop. Never drop the valid live index. Matching reloptions
alone do not prove construction completed: retry the pending migration so it always reindexes. Its rollback similarly
rebuilds at the previous `m=16, ef_construction=64`; reverting only the application binary leaves the stronger index
usable.
No application deployment or extension upgrade is performed automatically.

The opt-in Release benchmark checks retrieval recall against exact searches and measures full generation/paging on
100,000 synthetic 1,024D embeddings with 25 interest clusters:

```bash
GALLERY_RUN_PERFORMANCE=1 dotnet test GallerySiteIntegrationTests/GallerySiteIntegrationTests.csproj -c Release --filter FullyQualifiedName~Feed_HundredThousandDenseVectors_MeetsGenerationPagingAndRecallBudgets
```

Normal acceptance verifies the configured production search settings and records results/query plans in
`/tmp/gallery-for-me-indexed-benchmark.log`. Add `GALLERY_FOR_ME_CALIBRATE=1` to explicitly run the bounded diagnostic
calibration; its selected candidate still requires a subsequent normal acceptance run. The speed comparison
uses the old exact-ten **search phase**, excluding publication, against new 25-reference **full generation**, including
setup and publication; this is a conservative comparison. Synthetic recall measures retrieval fidelity, not whether
real users prefer the recommendations. Save a copy of the benchmark log after each run to retain its results.

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
