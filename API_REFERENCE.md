# Gallery API

## Guest browsing

The following existing GET routes accept requests without an access token:

| Route | Guest access |
| --- | --- |
| `/api/images` | Search and paginate guest-visible images. |
| `/api/images/{id}` | Image details and lifetime like/comment counts. |
| `/api/images/{id}/content` | Original, JPEG, or preview file after checking guest visibility. |
| `/api/images/{id}/recommendations` | Similar guest-visible images, starting from a guest-visible image. |
| `/api/images/{imageId}/comments` | Non-deleted comments on a guest-visible image. |
| `/api/tags/suggestions?query={prefix}` | Approved tags used on guest-visible images, ranked by eligible usage. |
| `/api/discovery/{collection}` | Daily discovery lists described below. |

A guest-visible image is an approved, undeleted, public (`Gallery`) `UserUpload` with an active, approved tag whose
normalized name is exactly `sfw`. Any attached `nsfw` tag blocks guest access, including a pending tag. A pending tag
replacement proposing `nsfw` also blocks access until resolved. Proposed or unapproved `sfw` tags do not grant access.
Existing uploads without an approved `sfw` tag stay hidden from guests; no tags are assigned automatically.

All search filters intersect with this restriction. For guests, `kind=All` and `kind=Fan` return eligible user uploads;
`kind=Official` returns an empty list. Existing sort, random seed, tag/excluded-tag, AI, and pagination behavior applies.
AI-generated images may qualify if they meet the same visibility rules. Counts and totals use only guest-visible images
before pagination. Guests always receive `isLikedByCurrentUser: false` and no pending tags.

Direct reads of an ineligible image return `404` for details, files, comments, and recommendations. An eligible image
whose recommendations are not ready retains the existing `409` response. Normal input validation still returns `400`.

Requests without bearer credentials use guest access. Supplying an invalid, expired, or revoked access token returns
`401`; clients can refresh their token or deliberately retry without credentials to browse as guests. Valid signed-in
users retain their existing broader gallery access and owner/staff privileges.

Guest responses use `Cache-Control: private, no-store`. All opened read routes use `Vary: Authorization`, including
signed-in responses, to separate viewer-dependent content. Previously downloaded files cannot be recalled.

There is no image/page viewing quota. Guest reads share a fixed-window, per-IP request budget, including image files,
with no queue. The default is 300 requests per minute **per API process**. Exhaustion returns `429` ProblemDetails and
`Retry-After` when available. Set `GuestBrowsing__RequestsPerMinute` for SDK/container deployments or
`GUEST_BROWSING_REQUESTS_PER_MINUTE` in the Compose `.env` file to change the limit (valid range 1–100000). Authenticated
reads are exempt from this guest policy; existing write/authentication limits still apply.

All writes, personal feeds, rankings, announcements, feedback, tag-change history, and administration retain their
existing authentication/authorization requirements. Response DTOs and route names are unchanged. No database migration
or new dependency is required. This change implements backend access only; guest frontend navigation is separate.

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
401. Successful signed-in responses use `Cache-Control: private`; guests use `private, no-store`. Both vary by
`Authorization`.

Only approved, non-hidden images with `visibility: "Gallery"` appear, including for staff. Most liked counts current
active likes created in the selected day; Most commented counts non-deleted comments created in that day. Both exclude
zero counts and can include older images; scraped images appear only for signed-in viewers. New uploads includes only `UserUpload` images uploaded in
the day. All date comparisons use inclusive UTC midnight and exclusive next UTC midnight.

Popularity lists sort by their daily count descending, upload time descending, then image ID descending. New uploads
sorts by upload time descending, then image ID descending. Paging and filtering happen in the database. Totals and
entries are read from one PostgreSQL repeatable-read snapshot.

Every response contains:

| Field                            | Meaning                                                            |
|----------------------------------|--------------------------------------------------------------------|
| `collection`                     | `most-liked`, `new-uploads`, or `most-commented`                   |
| `period`                         | `Daily`                                                            |
| `date`, `timeZone`               | Selected ISO calendar date and `UTC`                               |
| `periodStartUtc`, `periodEndUtc` | Inclusive start and exclusive end, serialized with `Z`             |
| `generatedAtUtc`                 | Server UTC time captured once for the request, serialized with `Z` |
| `galleryTotal`                   | All images discoverable by this viewer, across all dates, without AI filters       |
| `page`, `pageSize`               | Normalized pagination                                              |
| `total`                          | Collection count after date and AI filters, before pagination      |
| `entries`                        | Array of collection entries; always present, including when empty  |

Most liked entries contain `{ "image": GalleryImage, "periodLikeCount": number }`; Most commented entries contain
`{ "image": GalleryImage, "periodCommentCount": number }`; New uploads entries contain `{ "image": GalleryImage }`. The
image uses the existing complete `AppImageDto` contract, with lifetime like/comment counts and the authenticated user's
like state (false for guests). Pending tag names and storage keys are never included.

A page beyond the end returns an empty array while retaining the collection total. An empty daily collection can still
have a positive `galleryTotal`. Explicit past dates query currently stored likes and comments for that day; they do not
create or read ranking archives. Existing ranking behavior is unchanged. No database migration is required. The optional guest rate configuration is described above.

See [the homepage specification](GallerySiteBackend/homepage-api.md) for the frontend contract and daily collection
definitions.
