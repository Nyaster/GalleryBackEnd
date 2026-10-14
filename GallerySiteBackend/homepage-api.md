# Homepage API reference

This reference describes the LilGallery discovery homepage and its full collection views. The backend implements the three daily collections in this order: Most liked, New images uploaded today, then Most commented. Discovery now accepts guest requests. Guests see only approved, undeleted, public user uploads carrying an approved `sfw` tag; `nsfw`, including pending tags and pending tag replacements proposing `nsfw`, overrides that qualification. Signed-in discovery retains its existing broader image visibility. The frontend integration described below is the existing authenticated client; adapting frontend navigation to guests is outside this backend change.

The collection definitions below are implemented by the backend. See [the current API contract](../API_REFERENCE.md) for guest eligibility and rate-limit configuration.

## Existing behavior and missing support

The existing Tops page uses the ranking API separately. Its scoring, tie breakers, filtering, and weekly, monthly, and archived behavior remain outside this homepage change; the homepage requires only the three discovery reads below.

`GET /api/images` currently supports `Newest`, `Oldest`, `MediaId`, and `Random`. It has no date boundary filter, like sort, or comment sort. `GalleryImage` already supplies lifetime `likeCount`, visible lifetime `commentCount`, `uploadedAtUtc`, dimensions, uploader, tags, AI classification, and content URL. `GET /api/users/me/liked-images` is the current user's library, not the global Most liked collection.

The backend's existing day boundaries use UTC. No separate configurable site timezone was found. Return `UTC` as the site's effective timezone for this implementation; neither the developer's Europe/Warsaw timezone nor the visitor's device timezone determines today. A later site timezone setting should control discovery day boundaries without changing existing ranking semantics.

Evidence: [frontend contracts](../src/api/types.ts), [existing API client](../src/api/gallery.ts), [ranking navigation](../src/pages/rankingNavigation.ts), and the live `http://localhost:8080/openapi/v1.json`, inspected on 4 October 2026. Backend definitions were checked in `Entities/Models/RankingPeriodBounds.cs`, `Repository/RankingRepository.cs`, `Application/Features/Rankings/GetRankingsHandler.cs`, and `Shared/DataTransferObjects/SearchImageDto.cs` in the adjacent GalleryBackEnd project.

## Collection definitions

Every collection contains only gallery-visible, approved, non-hidden images. Guest collections additionally enforce SFW user-upload eligibility before aggregation and pagination. Apply the signed-in visibility predicate to ordinary users and staff; private, pending, rejected, and hidden images belong in their existing management flows.

| Section | Daily definition | Primary card metadata |
| --- | --- | --- |
| Most liked | Number of currently active likes whose `CreatedAtUtc` falls within the day, on images of any upload date. | `periodLikeCount`, labeled likes today. |
| New images uploaded today | `source = UserUpload` and `uploadedAtUtc` within the day; newest first. Scraped imports do not count as community uploads. | Upload time in the site timezone. |
| Most commented | Number of non-deleted comments whose `CreatedAtUtc` falls within the day, on images of any upload date. | `periodCommentCount`, labeled comments today. |

Most liked uses the current `ImageLikes` records, not all historical Like events. Removing a like added today removes it from this count; repeated unlike and re-like activity cannot inflate the number of active likes. Removing an older like does not reduce today's active new-like count. Lifetime image counts remain unchanged and available in the viewer.

Most liked and Most commented can include images uploaded on an earlier date. Only New uploads is restricted to images uploaded within the selected day.

Exclude zero-count entries from the two popularity collections. Order Most liked by `periodLikeCount DESC`, then `uploadedAtUtc DESC`, then `id DESC`. Order Most commented by `periodCommentCount DESC`, then `uploadedAtUtc DESC`, then `id DESC`. Order New uploads by `uploadedAtUtc DESC`, then `id DESC`. These new tie breakers are proposed.

## Proposed discovery endpoint

```http
GET /api/discovery/{collection}?page=1&pageSize=4
```

Allowed collection values are `most-liked`, `new-uploads`, and `most-commented`. All three use a daily scope in the initial version. A single collection endpoint allows the homepage to load and retry each section independently and lets full collection screens reuse the same definitions and pagination.

| Parameter | Contract |
| --- | --- |
| `collection` | Required path value from the three values above. |
| `page` | Optional integer, default 1. Clamp values below 1 to 1, matching current API behavior. |
| `pageSize` | Optional integer, default 20. Clamp to 1–50, matching the existing gallery/ranking APIs. Homepage requests 4; full lists request 20. |
| `date` | Optional `YYYY-MM-DD` calendar date in the site timezone. Default is the server's current site date. A specified past date queries current records for that day, without an immutable archive snapshot. Reject malformed or future dates with 400. |
| `aiUsage` | Optional repeated enum filter, with the same parsing and meanings as existing gallery and ranking endpoints. The homepage initially requests the unfiltered collections. |

Capture server time once at the start of a request. Compute local midnight and the next local midnight using the site's effective timezone, convert those instants to UTC, and query the half-open interval `timestamp >= periodStartUtc && timestamp < periodEndUtc`. Do not use the last 24 hours or assume that a future non-UTC day always has 24 hours. With the present UTC definition, 4 October 2026 is `[2026-10-04T00:00:00Z, 2026-10-05T00:00:00Z)`.

Return metadata and counts from the same logical read as the entries. Return period metrics in the collection response instead of requiring per-image like or comment requests.

```ts
type DiscoveryCollection = 'most-liked' | 'new-uploads' | 'most-commented'

interface DiscoveryResponse<TEntry> {
  collection: DiscoveryCollection
  period: 'Daily'
  date: string                 // YYYY-MM-DD in timeZone
  timeZone: string             // effective site timezone; currently UTC
  periodStartUtc: string       // ISO 8601 inclusive start
  periodEndUtc: string         // ISO 8601 exclusive end
  generatedAtUtc: string       // server time captured for this response
  galleryTotal: number         // all discoverable images, across all dates, without AI filters
  page: number
  pageSize: number
  total: number                // collection total after date and AI filters, before pagination
  entries: TEntry[]
}

interface MostLikedEntry {
  image: GalleryImage          // existing complete DTO, including lifetime likeCount
  periodLikeCount: number      // strictly positive active likes created in the day
}

interface NewUploadEntry {
  image: GalleryImage
}

interface MostCommentedEntry {
  image: GalleryImage          // existing complete DTO, including visible lifetime commentCount
  periodCommentCount: number   // strictly positive visible comments created in the day
}
```

Return the existing `GalleryImage` shape without removing fields, changing enum names, or replacing its lifetime counts with daily counts. `isLikedByCurrentUser` must reflect the authenticated user and is always false for guests. Never expose storage keys, private images, or pending tag names through these discovery collections.

## Example response

This example uses illustrative data, not current gallery statistics. The image's lifetime 120 likes and today's 20 active new likes intentionally differ.

```json
{
  "collection": "most-liked",
  "period": "Daily",
  "date": "2026-10-04",
  "timeZone": "UTC",
  "periodStartUtc": "2026-10-04T00:00:00Z",
  "periodEndUtc": "2026-10-05T00:00:00Z",
  "generatedAtUtc": "2026-10-04T14:30:00Z",
  "galleryTotal": 206,
  "page": 1,
  "pageSize": 4,
  "total": 1,
  "entries": [
    {
      "periodLikeCount": 20,
      "image": {
        "id": 42,
        "source": "UserUpload",
        "uploadedBy": "example-user",
        "uploadedAtUtc": "2026-09-20T10:00:00Z",
        "contentUrl": "/api/images/42/content",
        "tags": ["landscape"],
        "width": 1920,
        "height": 1080,
        "visibility": "Gallery",
        "moderationStatus": "Approved",
        "aiUsage": "HumanMade",
        "pendingTags": null,
        "hiddenAtUtc": null,
        "likeCount": 120,
        "commentCount": 32,
        "isLikedByCurrentUser": false
      }
    }
  ]
}
```

For Most commented, use the same envelope with `collection: "most-commented"` and entries shaped as `{ "periodCommentCount": 16, "image": { ... } }`. New uploads uses `collection: "new-uploads"` and entries shaped as `{ "image": { ... } }`. The nested image DTO is identical in all cases.

## Empty responses and request failures

Successful empty collections return 200, `total: 0`, and `entries: []`, with all scope metadata and `galleryTotal` present. A page beyond the end also returns an empty entries array but retains its positive collection `total`; it must not imply that the whole collection is empty. Missing or null entries are malformed responses, not empty collections.

If `galleryTotal > 0` and New uploads has `total: 0`, show “No images uploaded today yet. Be the first!” and a “Start uploading” link to `/upload`. Keep every other populated section visible. If all three daily collections are empty but `galleryTotal > 0`, the gallery still exists: keep the daily empty states and offer the full gallery.

Show the central “Be the first to share an image.” state only when an unfiltered, successful response authoritatively reports `galleryTotal: 0`, with no contradictory populated result. Recheck inconsistent counts instead of declaring the gallery empty. While the count is unknown, reserve the layout or show a retryable failure. An existing unfiltered `GET /api/images?page=1&pageSize=1` response can provide a fallback existence check through its `total`; no new statistics endpoint is required.

Preserve existing authenticated read behavior. Missing credentials select guest visibility. Return 401 for supplied invalid, expired, or revoked credentials, 400 for an invalid collection/date/filter, 429 for rate limiting with `Retry-After` when available, and the usual 5xx status for server failures. Use the existing ProblemDetails shape, including a trace ID when available. The frontend retains the standard session refresh flow and displays backend error descriptions verbatim. Use `Cache-Control: private` for signed-in reads and `Cache-Control: private, no-store` for guests; both vary by `Authorization`. Guest `galleryTotal` counts only guest-visible images across all dates and without AI filters.

The frontend issues the three reads independently; one failed collection displays its own Retry action and does not remove successful sections. A failed image-content download keeps its frame and accessible viewer link and displays an image-unavailable state. Neither failure is converted into an empty collection.

## Collection links and day changes

The proposed frontend full-list URLs are `/gallery?collection=most-liked&date=2026-10-04`, `/gallery?collection=new-uploads&date=2026-10-04`, and `/gallery?collection=most-commented&date=2026-10-04`. These query modes do not exist yet; the frontend will add them while preserving ordinary tag, kind, AI, sort, and pagination behavior. Each list uses the same collection definition and date as its homepage preview.

Use response boundaries and timezone for labels. Refresh the homepage at the next site midnight, and when the tab resumes after crossing that boundary. Cancel superseded requests. If discovery responses straddle midnight, refresh the affected batch so a single page does not show two different meanings of today.

## Backend acceptance checks

- Confirm that older images can appear in daily Most liked and Most commented.
- Check that removing a like added today reduces the daily active count, removing an older like does not, and unlike and re-like cycles cannot inflate Most liked.
- Include events at the day's start; exclude events at the next midnight. Exclude yesterday's events and events in future days.
- Exclude deleted comments from both daily and lifetime comment counts. Exclude private, pending, rejected, and hidden images in every discovery collection.
- Exclude scraped imports from New uploads; include only discoverable UserUpload images uploaded within the day. An image approved today but uploaded yesterday is not a new upload today.
- Return stable pagination, correct collection totals, and a separate unfiltered `galleryTotal` for sparse, empty, filtered, and out-of-range pages.
- Return correct current-user like state (false for guests), reject invalid credentials, enforce guest eligibility on counts and entries, and keep failed reads distinguishable from successful empty reads.

If the preferred backend API shape is an extension of `GET /api/images` rather than a new discovery endpoint, it can serve the same design by adding equivalent collection/date filters, daily metrics, scope metadata, and the global total. Do not sort a single newest page in the browser to approximate either global popularity collection.

## Frontend integration now implemented

- `/home` requests all three collections independently with `page=1&pageSize=4`, without a date so the backend selects its current site day.
- “View all” opens `/gallery?collection={collection}&date={response.date}`. Complete lists request `pageSize=20`, preserve the date while paging, and use backend order unchanged. The current collection UI has no AI filter; the optional API filter remains available for future consumers.
- The authenticated existing `GET /api/images?page=1&pageSize=1` is an independent existence check. Its unfiltered `total` permits the true gallery-empty state even when discovery endpoints are unavailable. Zero daily entries alone never means the gallery is empty.
- The client verifies the exact collection, `Daily` period, requested page and page size, valid date, valid IANA timezone, UTC timestamps with `Z`, complete day boundaries, integer totals, and the expected number of entries for that page. It checks approved, visible, nonhidden image records, valid dimensions/content URLs, and strictly positive daily counts for popularity lists. Malformed payloads display a request error, not an empty state.
- Return all required `GalleryImage` fields, including `source`, `uploadedBy` (nullable), `uploadedAtUtc`, `contentUrl`, `tags`, `width`, `height`, `visibility`, `moderationStatus`, `aiUsage`, `likeCount`, `commentCount`, and `isLikedByCurrentUser`. For New uploads, source must be `UserUpload` and upload time must fall within the returned day.
- The homepage uses `generatedAtUtc` and `periodEndUtc` to refresh at the server-defined day boundary, compensating for the browser's clock. It checks the deadline again on focus/visibility and reloads mismatched day or timezone responses. Complete lists with an explicit date stay on that date; older days use “on this day” labels.
- Existing content previews use the existing authenticated `contentUrl` with `format=preview`. Cards preserve viewer origin, list sequence, and return scroll position.

The backend now implements these reads. Frontend integration should check signed-in `/home`, all three View all lists, an inactive day with a populated gallery, genuine global emptiness, and a configured non-UTC day including daylight-saving transitions. The earlier frontend checks used controlled responses; backend aggregation and guest boundaries are covered by the integration suite. Frontend guest navigation still requires separate implementation.
