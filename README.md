# Gallery backend

See [API_REFERENCE.md](API_REFERENCE.md) for the frontend integration contract.

The API targets .NET 10, PostgreSQL with the `vector` extension, and a persistent local image volume. It applies its initial EF migration at startup and creates an administrator only when both `BootstrapAdmin__Login` and `BootstrapAdmin__Password` are configured.

## Run locally

Copy `.env.example` to `.env`, replace every placeholder, then run:

```bash
docker compose up --build
```

The API is exposed on `http://localhost:8080`; PostgreSQL and uploaded files persist in named volumes. The local compose setup disables ONNX embeddings and marks the refresh cookie non-Secure solely because it uses HTTP. Enable TLS and set `JwtConfig__RefreshCookieSecure=true` in every deployed environment. To enable embeddings, mount the model at `/app/Data/model/model.onnx` and set `Embedding__Enabled=true`.

`ADMIN_LOGIN` and `ADMIN_PASSWORD` create the initial administrator when the database is first started. To enable scraping, set `SCRAPER_ENABLED=true`, `SCRAPER_LOGIN`, and `SCRAPER_PASSWORD` in `.env`; these credentials are used only to sign in to the configured source site. Restart the API after changing them.

For local SDK development, set `ConnectionStrings__DefaultConnection`, `JwtConfig__ValidIssuer`, `JwtConfig__ValidAudience`, and `JwtConfig__SecretKey` in `secrets.json` or environment variables. `secrets.json` can also contain `BootstrapAdmin` and `ParserSettings` sections; keep `ParserSettings:Enabled` false until valid source-site credentials are provided. Then run:

```bash
dotnet restore GallerySiteBackend.sln
dotnet build GallerySiteBackend.sln
dotnet test GallerySiteUnitTests/GallerySiteUnitTests.csproj
dotnet run --project GallerySiteBackend/GallerySiteBackend.csproj
```

## API behavior

All gallery endpoints require a bearer access token. `POST /api/auth/login` and `POST /api/auth/register` return a short-lived access token and issue a rotating, Secure/HttpOnly refresh cookie. `POST /api/auth/refresh` reads that cookie; refresh tokens are never returned in JSON.

User uploads begin as `Pending`; only an administrator can approve or reject them. Gallery search and recommendations include only approved, non-private images. The uploader and administrators can still read pending or private images directly. API errors are RFC 7807 problem-details responses.

## One-time scrape

After configuring scraper credentials and starting the API, log in with the bootstrap admin and copy `accessToken` from the response. Queue a small scrape with `Incremental` mode:

```bash
curl -X POST http://localhost:8080/api/admin/scrape-runs \
  -H 'Authorization: Bearer <adminAccessToken>' \
  -H 'Content-Type: application/json' \
  -d '{"mode":"Incremental"}'
```

The response is `202 Accepted` and contains the run `id`. Check its progress until it is `Completed`:

```bash
curl http://localhost:8080/api/admin/scrape-runs/<runId> \
  -H 'Authorization: Bearer <adminAccessToken>'
```

Use `Incremental` for a one-time sample; it fetches at most `ParserSettings:IncrementalPages` pages (five by default). `Full` imports every available page and should be used only when that larger run is intended.
