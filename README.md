# .NET SDK for [Visual Regression Tracker](https://github.com/Visual-Regression-Tracker/Visual-Regression-Tracker)

[![NuGet version](https://img.shields.io/nuget/v/VisualRegressionTracker)](https://www.nuget.org/packages/VisualRegressionTracker)
[![CI](https://github.com/Visual-Regression-Tracker/sdk-dotnet/actions/workflows/workflow.yml/badge.svg)](https://github.com/Visual-Regression-Tracker/sdk-dotnet/actions/workflows/workflow.yml)

## Install

Requires .NET 10 and a VRT server 6.0 or later.

```sh
dotnet add package VisualRegressionTracker
```

## Configuration

| Option (`Config`) | Environment variable | `vrt.json` key | Default | Meaning |
|---|---|---|---|---|
| `ApiUrl` | `VRT_APIURL` | `apiUrl` | `http://localhost:4200` | URL of the VRT API |
| `ApiKey` | `VRT_APIKEY` | `apiKey` | required | API key of a VRT user |
| `Project` | `VRT_PROJECT` | `project` | required | project name or ID |
| `BranchName` | `VRT_BRANCHNAME` | `branchName` | required | current git branch |
| `CiBuildId` | `VRT_CIBUILDID` | `ciBuildId` | none | CI build ID or commit SHA; a number is accepted in `vrt.json` |
| `EnableSoftAssert` | `VRT_ENABLESOFTASSERT` | `enableSoftAssert` | `false` | log a failed check instead of throwing; `true`, `1` (any case for `true`) |
| `SourceBranch` | `VRT_SOURCEBRANCH` | `sourceBranch` | none | branch to compare against instead of the project's main branch |
| `HttpTimeoutInSeconds` | `VRT_HTTPTIMEOUTINSECONDS` | `httpTimeoutInSeconds` | `360` | how long to wait for each response; `0` waits without limit, at most `2147483` |

A `Config` passed to the constructor is used as it is. Without one, the tracker reads
`vrt.json` from the current directory (or the path given to `Config.GetDefault(path)`),
then environment variables override it field by field.

As code:

```csharp
var config = new Config
{
    ApiUrl = "http://localhost:4200",
    Project = "Default project",
    ApiKey = "tXZVHX0EA4YQM1MGDD",
    CiBuildId = "commit_sha",
    BranchName = "develop",
    EnableSoftAssert = false,
    SourceBranch = "main",
    HttpTimeoutInSeconds = 360,
};
using var vrt = new VisualRegressionTracker.VisualRegressionTracker(config);
```

As `vrt.json` (comments, trailing commas and any property case are accepted):

```json
{
  "apiUrl": "http://localhost:4200",
  "project": "Default project",
  "apiKey": "tXZVHX0EA4YQM1MGDD",
  "ciBuildId": "commit_sha",
  "branchName": "develop",
  "enableSoftAssert": false,
  "sourceBranch": "main",
  "httpTimeoutInSeconds": 360
}
```

As environment variables:

```sh
export VRT_APIURL="http://localhost:4200"
export VRT_PROJECT="Default project"
export VRT_APIKEY="tXZVHX0EA4YQM1MGDD"
export VRT_CIBUILDID="commit_sha"
export VRT_BRANCHNAME="develop"
export VRT_ENABLESOFTASSERT=true
export VRT_SOURCEBRANCH="main"
export VRT_HTTPTIMEOUTINSECONDS=360
```

```csharp
using var vrt = new VisualRegressionTracker.VisualRegressionTracker();
```

The default timeout is long because the server compares the images, possibly with a VLM,
before it answers `Track`. When you do not pass an `HttpClient`, the tracker creates its
own with `HttpTimeoutInSeconds` and a 10 s connect timeout, and disposing the tracker
disposes that client. A client you pass in keeps its own settings and is left open.

## Usage

Create one tracker for the whole test run, start a build, track images and stop the build:

```csharp
using VisualRegressionTracker;

using var vrt = new VisualRegressionTracker.VisualRegressionTracker();
await using (await vrt.Start())
{
    await vrt.Track("Home page", screenshot);
}
```

Disposing the value `Start` returns stops the build. Without `using`, call `await vrt.Stop()`.

`Track` takes the image as a base64 string (sent as JSON), or as a `byte[]`, a `Stream` or
a `FileInfo` (sent as `multipart/form-data`, which is smaller and has its own server limit,
`MULTIPART_FILE_SIZE_LIMIT`). The optional arguments are:

```csharp
await vrt.Track(
    "Home page",
    screenshot,
    os: "Mac",
    browser: "Chrome",
    viewport: "800x600",
    device: "PC",
    customTags: "smoke",
    diffTollerancePercent: 1,
    comment: "after the header redesign",
    ignoreAreas: new[] { new IgnoreAreaDto { X = 10, Y = 20, Width = 300, Height = 400 } },
    merge: false);
```

`diffTollerancePercent` is the allowed share of different pixels in percent (spelled as in
the VRT API). Each `IgnoreAreaDto` is a rectangle in pixels from the top-left corner.
`merge` is passed to the server as is and left out when not given.

With [Microsoft Playwright](https://playwright.dev/dotnet/):

```csharp
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync();
var page = await browser.NewPageAsync();
await page.GotoAsync("https://github.com/Visual-Regression-Tracker/sdk-dotnet");
await vrt.Track("sdk-dotnet", await page.ScreenshotAsync());
```

## Result and statuses

`Track` returns a `TestRunResult` with the `Status`, the link to the test run in the web
app (`Url`) and the `ImageUrl`, `DiffUrl` and `BaselineUrl` of its images
(`{apiUrl}/images/{name}`, or `null` when the server returns no image).

| `Status` | Meaning | `Track` |
|---|---|---|
| `New` | no baseline yet | fails |
| `Unresolved` | difference found | fails |
| `Ok` | matches the baseline | returns |
| `AutoApproved` | approved automatically | returns |
| `Approved` | approved | returns |
| `Failed` | rejected | returns |
| `Unknown` | a status this SDK does not know | returns, with a warning on the standard error stream |

A failing status throws a `VisualRegressionTrackerError`, or with `EnableSoftAssert`
writes the message to the standard error stream and returns the result.

## Errors

- The server rejects a request: `ServerError`, a `VisualRegressionTrackerError` with the
  HTTP `StatusCode` and the server's `ServerMessage`. 413 and 422 add a hint about the
  server's body and image size limits.
- The server is busy (503 with `Retry-After`, for example a full image diff queue): the
  request is repeated after the given time, at most 3 times and waiting at most 60 s each
  time, then `ServerError` is thrown. A 503 without `Retry-After` is not repeated.
- Network failure, timeout or a response that is not from the VRT API:
  `VisualRegressionTrackerError`.
- Incomplete configuration: `MissingConfigurationError` naming the field.
- A `vrt.json` that cannot be read: `VisualRegressionTrackerError` naming the file.

## Upgrading from 4.x

5.0.0 works with VRT server 6.0 and has these breaking changes:

- It targets .NET 10 (`net10.0`) only.
- The NSwag-generated `ApiClient`, `ApiException`, `ApiException<T>` and DTOs are gone.
  `IgnoreAreaDto` stays, without `AdditionalProperties`.
- Server errors throw `ServerError` instead of `ApiException`.
- Only `New` and `Unresolved` fail `Track`. `Failed` and `Approved` are returned instead
  of thrown, and a status this SDK does not know is returned as `Unknown`.
- `ImageUrl`, `DiffUrl` and `BaselineUrl` point to `{apiUrl}/images/{name}`.
- `vrt.json` is read with System.Text.Json: single quotes no longer work.
- `VisualRegressionTracker` is `IDisposable`.

## Releasing

Merging to `main` releases. [semantic-release](https://semantic-release.gitbook.io/) reads the
[Conventional Commits](https://www.conventionalcommits.org/) since the last tag: `fix:`, `perf:` and `revert:`
make a patch, `feat:` a minor, and `!` after the type (`feat!:`) or a `BREAKING CHANGE:` footer a major
release. Other types (`chore:`, `ci:`, `docs:`, ...) release nothing. A squash merge takes the PR title (or, for a
single-commit PR, that commit's message) as its subject, so keep both conventional.

The `Release` workflow runs the tests, tags the commit (`5.1.0`, no `v`), packs the SDK with that
version (`-p:Version`), publishes it to NuGet through
[trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) and then
publishes the GitHub release with the generated notes and the `.nupkg`. Nothing is committed back.
It needs a trusted publishing policy on nuget.org for this repository, workflow `release.yml` and
environment `nuget`, and the `NUGET_USER` repository variable holding the nuget.org user name that
owns the policy.

If publishing fails, the tag and a draft release already exist: re-run the failed jobs of that run
(not the whole run) before anything else is merged.

Release notes are on [GitHub Releases](https://github.com/Visual-Regression-Tracker/sdk-dotnet/releases);
[CHANGELOG.md](CHANGELOG.md) holds the history up to 5.0.0.

### Release candidates

Every push to `next` releases a pre-release of the next version, tagged `X.Y.Z-rc.N`
(`6.0.0-rc.1` once a breaking change is in).
It goes to NuGet as a SemVer 2 pre-release, so NuGet clients keep installing the last stable release
unless pre-releases are included (`dotnet add package VisualRegressionTracker --prerelease`).
Its GitHub release is marked as a pre-release.

Breaking changes go into `next` through pull requests that target it. Merge `main` into `next` when
`main` gets new commits.

To release the final version, merge `next` into `main` with a merge commit, not a squash, so that
semantic-release still sees the breaking commits. That push releases it. The repository deletes merged
branches, so create `next` again from `main` before the next round of candidates.

## Links

- [Visual Regression Tracker](https://github.com/Visual-Regression-Tracker/Visual-Regression-Tracker)
- [Example with Playwright and NUnit](https://github.com/Visual-Regression-Tracker/examples-dotnet)
- [Other SDKs and agents](https://github.com/Visual-Regression-Tracker/Visual-Regression-Tracker#integration)
- [License: Apache-2.0](LICENSE)
