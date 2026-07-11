# Gallery backend

The API targets .NET 10, PostgreSQL with the `vector` extension, and a persistent local image volume. It applies its initial EF migration at startup and creates an administrator only when both `BootstrapAdmin__Login` and `BootstrapAdmin__Password` are configured.

## Run locally

Copy `.env.example` to `.env`, replace every placeholder, then run:

```bash
docker compose up --build
```

The API is exposed on `http://localhost:8080`; PostgreSQL and uploaded files persist in named volumes. The local compose setup disables ONNX embeddings and marks the refresh cookie non-Secure solely because it uses HTTP. Enable TLS and set `JwtConfig__RefreshCookieSecure=true` in every deployed environment. To enable embeddings, mount the model at `/app/Data/model/model.onnx` and set `Embedding__Enabled=true`.

For local SDK development, set `ConnectionStrings__DefaultConnection`, `JwtConfig__ValidIssuer`, `JwtConfig__ValidAudience`, and `JwtConfig__SecretKey` in `secrets.json` or environment variables, then run:

```bash
dotnet restore GallerySiteBackend.sln
dotnet build GallerySiteBackend.sln
dotnet test GallerySiteUnitTests/GallerySiteUnitTests.csproj
dotnet run --project GallerySiteBackend/GallerySiteBackend.csproj
```

## API behavior

All gallery endpoints require a bearer access token. `POST /api/auth/login` and `POST /api/auth/register` return a short-lived access token and issue a rotating, Secure/HttpOnly refresh cookie. `POST /api/auth/refresh` reads that cookie; refresh tokens are never returned in JSON.

User uploads begin as `Pending`; only an administrator can approve or reject them. Gallery search and recommendations include only approved, non-private images. The uploader and administrators can still read pending or private images directly. API errors are RFC 7807 problem-details responses.
