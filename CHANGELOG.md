# Changelog

Later versions: [GitHub releases](https://github.com/Visual-Regression-Tracker/sdk-dotnet/releases).

## 5.0.0

Works with VRT server 6.0 and targets .NET 10.

### Fixes
- `Stop` sends `{"isRunning": false}`, so the server stops the build itself instead of
  relying on its fallback for an empty request.
- `ImageUrl`, `DiffUrl` and `BaselineUrl` point to `{apiUrl}/images/{name}`, which
  works for HDD and S3 storage. They used to be built from the web-app link.
- Server errors throw `ServerError` with `StatusCode` and the server's message, with a
  hint for 413 (`BODY_PARSER_JSON_LIMIT`) and 422 (`MAX_IMAGE_PIXELS`). Network errors,
  timeouts and non-API responses are wrapped in `VisualRegressionTrackerError`.
- All six server statuses are known, an unknown one is returned as `Unknown`, and only
  `New` and `Unresolved` fail `Track`.
- With `EnableSoftAssert`, failures are written to stderr instead of being dropped.
- `Track(Stream)` leaves the caller's stream open.
- The `HttpClient` the tracker creates waits up to 360 s for slow comparisons.
- No dependencies: NSwag and Newtonsoft.Json 12.0.2 (CVE-2024-21907) are gone.

### Breaking changes
- Targets `net10.0` only.
- The generated `ApiClient`, `ApiException`, `ApiException<T>` and DTOs are removed;
  `IgnoreAreaDto` stays, without `AdditionalProperties`.
- `Track` throws `ServerError` instead of `ApiException`.
- `Failed`, `Approved` and `Unknown` are returned instead of thrown.
- `ImageUrl` is `null` when the server returns no image name.
- `vrt.json` is read with System.Text.Json: single quotes are no longer accepted, and an
  unreadable file throws `VisualRegressionTrackerError`.
- `VisualRegressionTracker` is `IDisposable`.

See "Upgrading from 4.x" in the README.
