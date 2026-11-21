# Gallery API

## Guest browsing

The following existing GET routes accept requests without an access token:

| Route                                  | Guest access                                                          |
|----------------------------------------|-----------------------------------------------------------------------|
| `/api/images`                          | Search and paginate guest-visible images.                             |
| `/api/images/{id}`                     | Image details and lifetime like/comment counts.                       |
| `/api/images/{id}/content`             | Original, JPEG, or preview file after checking guest visibility.      |
| `/api/images/{id}/recommendations`     | Similar guest-visible images, starting from a guest-visible image.    |
| `/api/images/{imageId}/comments`       | Non-deleted comments on a guest-visible image.                        |
| `/api/tags/suggestions?query={prefix}` | Approved tags used on guest-visible images, ranked by eligible usage. |
| `/api/discovery/{collection}`          | Daily discovery lists described below.                                |

A guest-visible image is an approved, undeleted, public (`Gallery`) `UserUpload` with an active, approved tag whose
normalized name is exactly `sfw`. Any attached `nsfw` tag blocks guest access, including a pending tag. A pending tag
replacement proposing `nsfw` also blocks access until resolved. Proposed or unapproved `sfw` tags do not grant access.
Existing uploads without an approved `sfw` tag stay hidden from guests; no tags are assigned automatically.

All search filters intersect with this restriction. For guests, `kind=All` and `kind=Fan` return eligible user uploads;
`kind=Official` returns an empty list. Existing sort, random seed, tag/excluded-tag, AI, and pagination behavior
applies.
AI-generated images may qualify if they meet the same visibility rules. Counts and totals use only guest-visible images
before pagination. Guests always receive `isLikedByCurrentUser: false` and no pending tags.

Direct reads of an ineligible image return `404` for details, files, comments, and recommendations. An eligible image
whose recommendations are not ready retains the existing `409` response. Normal input validation still returns `400`.

Requests without bearer credentials use guest access. Supplying an invalid or expired access token returns
`401`; clients can refresh their token or deliberately retry without credentials to browse as guests. Valid signed-in
users retain their existing broader gallery access and owner/staff privileges.

Access tokens are validated locally. Tokens carrying the Admin or Moderator role also require a database check
that the account still exists. Account recovery revokes refresh sessions immediately,
but already-issued access tokens remain valid until expiry (`JwtConfig:AccessTokenMinutes`, default 15 minutes,
plus 30 seconds of validation clock skew). The legacy `auth_version` claim does not revoke access tokens.

These read routes use `Cache-Control: private, no-store` and `Vary: Authorization` for guest and signed-in viewers.
Previously downloaded files cannot be recalled.

There is no image/page viewing quota. Guest reads share a fixed-window, per-IP request budget, including image files,
with no queue. The default is 300 requests per minute **per API process**. Exhaustion returns `429` ProblemDetails and
`Retry-After` when available. Set `GuestBrowsing__RequestsPerMinute` for SDK/container deployments or
`GUEST_BROWSING_REQUESTS_PER_MINUTE` in the Compose `.env` file to change the limit (valid range 1–100000).
Authenticated
reads are exempt from this guest policy; existing write/authentication limits still apply.

All writes, personal feeds, rankings, announcements, feedback, tag-change history, and administration require
authentication.

## Display all content

Registration, login, and refresh return `user.displayAllContent`. `GET /api/auth/me` returns the field directly.
It defaults to `false` for new and existing accounts. The settings switch sends
`PUT /api/users/me/content-preferences` with `{"displayAllContent": true}` or `{"displayAllContent": false}`.
The response is the updated user profile. An omitted or null value returns `400`. Reload the current view after
saving. The existing access token works across the change, and image endpoints read the saved setting themselves.

When the switch is off, an image is hidden if its approved, attached tags contain **every tag in any one rule**.
For example, rules `[10]` and `[20, 30]` hide images carrying tag 10 or both 20 and 30. Tag 20 alone still qualifies.
Guests always follow these rules in addition to guest eligibility. Enabling the switch skips the admin tag rules
while retaining ordinary image permissions. Staff follow their profile during browsing. Explicit staff moderation
responses provide a staff-only `contentUrl` under `/api/admin/images/{id}/content`.

Filtering applies before list totals and pagination, including discovery, live and archived rankings, uploads,
likes, and recommendations. Hidden image IDs return `404` for details, files, comments, and recommendations.
Image files use `Cache-Control: private, no-store`. Stored ForMe pages enforce current rules and settings while
retaining their generation-time total. Start a fresh feed after opting in to include additional images.

### Admin rule management

Only admins can manage rules. Use approved tag IDs from `GET /api/admin/tags?status=Approved`.

| Method | Route                                      | Result                                                             |
|--------|--------------------------------------------|--------------------------------------------------------------------|
| GET    | `/api/admin/content-visibility-rules`      | List rules as `{id, tagIds}`.                                      |
| GET    | `/api/admin/content-visibility-rules/{id}` | Read a single rule.                                                |
| POST   | `/api/admin/content-visibility-rules`      | Create with `{"tagIds":[20,30]}`. Returns `201` and the rule.      |
| PUT    | `/api/admin/content-visibility-rules/{id}` | Replace the complete tag group atomically. Returns the saved rule. |
| DELETE | `/api/admin/content-visibility-rules/{id}` | Remove the rule. Returns `204`.                                    |

Use at least one distinct, positive, approved tag ID. Invalid groups return `400`; duplicating an existing group
in any order returns `409`. Unknown rule IDs return `404`. Changes take effect on subsequent requests.

## Discovery API

Discovery reads support guest browsing and authenticated sessions. The homepage loads these collections
independently:

```http
GET /api/discovery/most-liked?page=1&pageSize=4
GET /api/discovery/new-uploads?page=1&pageSize=4
GET /api/discovery/most-commented?page=1&pageSize=4
```

Each route accepts `page` (default 1, minimum 1), `pageSize` (default 20, clamped to 1–50), optional `date=YYYY-MM-DD`,
and repeated `aiUsage` filters using the existing enum names (`Unknown`, `HumanMade`, `AiAssisted`, `AiGenerated`). The
default date is the server's current UTC calendar day. Invalid collections, malformed dates, future dates, and invalid
AI filters return 400 ProblemDetails. Missing credentials select guest visibility; invalid or expired credentials return

401. Successful responses use `Cache-Control: private, no-store` for all viewers and vary by
     `Authorization`.

Only approved, non-hidden images with `visibility: "Gallery"` appear, including for staff. Most liked counts current
active likes created in the selected day; Most commented counts non-deleted comments created in that day. Both exclude
zero counts and can include older images; scraped images appear only for signed-in viewers. New uploads includes only
`UserUpload` images uploaded in
the day. All date comparisons use inclusive UTC midnight and exclusive next UTC midnight.

Popularity lists sort by their daily count descending, upload time descending, then image ID descending. New uploads
sorts by upload time descending, then image ID descending. Paging and filtering happen in the database. Totals and
entries are read from one PostgreSQL repeatable-read snapshot.

Every response contains:

| Field                            | Meaning                                                                      |
|----------------------------------|------------------------------------------------------------------------------|
| `collection`                     | `most-liked`, `new-uploads`, or `most-commented`                             |
| `period`                         | `Daily`                                                                      |
| `date`, `timeZone`               | Selected ISO calendar date and `UTC`                                         |
| `periodStartUtc`, `periodEndUtc` | Inclusive start and exclusive end, serialized with `Z`                       |
| `generatedAtUtc`                 | Server UTC time captured once for the request, serialized with `Z`           |
| `galleryTotal`                   | All images discoverable by this viewer, across all dates, without AI filters |
| `page`, `pageSize`               | Normalized pagination                                                        |
| `total`                          | Collection count after date and AI filters, before pagination                |
| `entries`                        | Array of collection entries; always present, including when empty            |

Most liked entries contain `{ "image": GalleryImage, "periodLikeCount": number }`; Most commented entries contain
`{ "image": GalleryImage, "periodCommentCount": number }`; New uploads entries contain `{ "image": GalleryImage }`. The
image uses the existing complete `AppImageDto` contract, with lifetime like/comment counts and the authenticated user's
like state (false for guests). Pending tag names and storage keys are never included.

A page beyond the end returns an empty array while retaining the collection total. An empty daily collection can still
have a positive `galleryTotal`. Explicit past dates query currently stored likes and comments for that day; they do not
create or read ranking archives. Existing ranking behavior is unchanged. No database migration is required. The optional
guest rate configuration is described above.

See [the homepage specification](GallerySiteBackend/homepage-api.md) for the frontend contract and daily collection
definitions.

## Personalized ForMe feed

```http
GET /api/images/for-me?pageSize=20&aiUsage=HumanMade&excludedTags=landscape
GET /api/images/for-me?cursor=<nextCursor>
```

This route requires a valid authenticated session. Initial requests accept `pageSize` (default 20, clamped to 1–50),
repeated `aiUsage` values using the existing classifications, and repeated `excludedTags`. Continuations accept only a
nonempty `cursor`; page size and filters cannot change within a feed. The response contains `mode`, `generatedAtUtc`,
`expiresAtUtc`, `pageSize`, `total`, `images`, and nullable `nextCursor`. Responses use
`Cache-Control: private, no-store`
and `Vary: Authorization`.

Generation selects up to `ForMeFeed:RepresentativeCount` diverse reference images from the latest 100 eligible likes
with embeddings. The startup-only limit defaults to 25 and must be between 1 and 100. Override with
`ForMeFeed__RepresentativeCount`, or `FOR_ME_REPRESENTATIVE_COUNT` in Compose `.env`, and restart the API. This is a
server setting, not a query parameter. Increasing it broadens interest coverage and increases database work; it does
not guarantee improved subjective relevance.

Each reference retrieves up to 500 eligible candidates using an index-friendly distance query, with or without filters.
Exact distances order each shortlist before the results are merged into at most 500 unique images.
An undersized merge can receive one exact top-up using the first reference. Only approved, non-hidden public gallery
images matching the filters qualify; all of the user's current likes are excluded. With no usable liked references or
no eligible embedded candidates, the existing lifetime-popularity fallback also considers images without embeddings.

Generation resolves currently approved requested tag names within its database snapshot, omitting an unnecessary
tag predicate when none match. The original requested names remain stored for cursor pages, including names that
become approved or attached later.

Feeds expire after one hour. Cursor pages read the stored order while suppressing images that subsequently become
ineligible or liked; `total` remains the original stored count. Old cursors continue to work after a configuration
change. The existing two-generator admission limit, request rate limits, ten-second generation deadline, and cursor
validation apply. Excess generation load returns 429; the server deadline returns 503 without publishing a partial feed.

New feed creation requires a released pgvector version at least 0.8.0 for iterative index scans. Unsupported versions
fail creation with an operator-facing upgrade error in server logs; existing cursor reads do not need new vector
searches. The API does not automatically upgrade extensions. Apply `ImproveForMeHnswConstruction` before rollout; it
concurrently rebuilds the existing HNSW index with `m=16, ef_construction=128`. See README for migration timeout and
recovery guidance. Routes and response fields remain unchanged.
