# .Net SDK for [Visual Regression Tracker](https://github.com/Visual-Regression-Tracker/Visual-Regression-Tracker)

[![NuGet version](https://img.shields.io/nuget/v/VisualRegressionTracker)](https://www.nuget.org/packages/VisualRegressionTracker)
[![Codacy Badge](https://app.codacy.com/project/badge/Grade/0b98190cea064d6f9f1210da653ba37b)](https://www.codacy.com/gh/Visual-Regression-Tracker/sdk-dotnet?utm_source=github.com&amp;utm_medium=referral&amp;utm_content=Visual-Regression-Tracker/sdk-dotnet&amp;utm_campaign=Badge_Grade)
[![Codacy Badge](https://app.codacy.com/project/badge/Coverage/0b98190cea064d6f9f1210da653ba37b)](https://www.codacy.com/gh/Visual-Regression-Tracker/sdk-dotnet?utm_source=github.com&utm_medium=referral&utm_content=Visual-Regression-Tracker/sdk-dotnet&utm_campaign=Badge_Coverage)

## Install

Requires .NET 10.

```sh
dotnet add package VisualRegressionTracker
```

## Upgrading from 4.x

5.0.0 works with VRT server 6.0 and has these breaking changes:

- It targets .NET 10 (`net10.0`) only.
- The NSwag-generated `ApiClient`, `ApiException`, `ApiException<T>` and DTOs are gone.
  `IgnoreAreaDto` stays, without `AdditionalProperties`.
- Server errors throw `ServerError` (a `VisualRegressionTrackerError` with `StatusCode`
  and `ServerMessage`) instead of `ApiException`.
- Only `New` and `Unresolved` fail `Track`. `Failed` and `Approved` are returned instead
  of thrown, and a status this SDK does not know is returned as `Unknown`.
- `ImageUrl`, `DiffUrl` and `BaselineUrl` point to `{apiUrl}/images/{name}` (they were
  wrong before) and are `null` when the server returns no image name.
- `vrt.json` is read with System.Text.Json: comments, trailing commas, any property case,
  a numeric `ciBuildId` and `"true"`/`"1"` for `enableSoftAssert` still work, but single
  quotes do not. A file that cannot be read throws a `VisualRegressionTrackerError`
  naming it.
- `VisualRegressionTracker` is `IDisposable`; see [Setup / Teardown](#setup--teardown).

## Usage

### Import

```csharp
using VisualRegressionTracker;
```

### Configure connection

#### As code
```csharp
config = new Config {
    // apiUrl - URL where backend is running 
    ApiUrl="http://localhost:4200",

    // project - Project name or ID
    Project="Default project",

    // apiKey - User apiKey
    ApiKey="tXZVHX0EA4YQM1MGDD",

    // ciBuildId - Current git commit SHA
    CiBuildId="commit_sha",

    // branch - Current git branch 
    BranchName="develop",

    // enableSoftAssert - Log errors instead of exceptions
    EnableSoftAssert=false,
};

vrt = new VisualRegressionTracker(config);
```

#### Or, as JSON config file `vrt.json`
```json
{
    "apiUrl":"http://localhost:4200",
    "project":"Default project",
    "apiKey":"tXZVHX0EA4YQM1MGDD",
    "ciBuildId":"commit_sha",
    "branchName":"develop",
    "enableSoftAssert":false
}
```
```csharp
vrt = new VisualRegressionTracker();
```

#### Or, as environment variables
```sh
export VRT_APIURL="http://localhost:4200"
export VRT_PROJECT="Default project"
export VRT_APIKEY="tXZVHX0EA4YQM1MGDD"
export VRT_CIBUILDID="commit_sha"
export VRT_BRANCHNAME="develop"
export VRT_ENABLESOFTASSERT=true
```
```csharp
vrt = new VisualRegressionTracker();
```

### Setup / Teardown

Create one tracker for the whole test run and dispose it at the end. When you do not
pass an `HttpClient`, the tracker creates its own with a 360 s timeout, because the
server compares the images while it answers `Track`, and disposing the tracker disposes
that client. A client you pass in keeps its own timeout and is left open.

```csharp
using var vrt = new VisualRegressionTracker.VisualRegressionTracker(config);
```

```csharp
await using (await vrt.Start())
{
    ...
    // track test runs
    ...
}
```

Without using:
```csharp
await vrt.Start()
...
// track test runs
...
await vrt.Stop()
```

### Assert

```csharp
await vrt.Track(
    // Name to be displayed
    "Image name",

    // Base64 encoded string, byte[] or Stream
    image,

    // Allowed mismatch tollerance in %
    // Default: 0%
    diffTollerancePercent: 1,

    // Optional
    os: "Mac",
    browser: "Chrome",
    viewport: "800x600",
    device: "PC",
    comment: "test comment",

    // Array of areas to be ignored
    ignoreAreas: new[] {
        new IgnoreAreaDto {
            // X-coordinate relative of left upper corner
            X = 10,
            // Y-coordinate relative of left upper corner
            Y = 20,
            // Area width in px
            Width = 300,
            // Area height in px
            Height = 400
        }
    }
);
```

`Track` returns a `TestRunResult` with the `Status`, the link to the test run in the
web app (`Url`) and the `ImageUrl`, `DiffUrl` and `BaselineUrl` of its images.

`New` (no baseline yet) and `Unresolved` (difference found) fail the check: `Track`
throws a `VisualRegressionTrackerError`, or with `EnableSoftAssert` writes the message
to the standard error stream and returns the result. Every other status, including
`Unknown` for a status this SDK does not know, is returned.

When the server rejects a request, `Track`, `Start` and `Stop` throw a `ServerError`
with the HTTP `StatusCode` and the server's `ServerMessage`.

### Example with [Microsoft Playwright](https://playwright.dev/dotnet/)

#### Imports
```csharp
using Microsoft.Playwright;
using VisualRegressionTracker;
```

#### Capture a screenshot using Playwright
```csharp
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync();
var page = await browser.NewPageAsync();
await page.GotoAsync("https://github.com/Visual-Regression-Tracker/sdk-dotnet/tree/main");
var screenshot = await page.ScreenshotAsync();
```

#### Track changes using Visual Regression Tracker
```csharp
var vrt = new VisualRegressionTracker.VisualRegressionTracker();
await using var cleanup = await vrt.Start();
await vrt.Track("sdk-dotnet", screenshot);
```

## Releasing

1. Set `<Version>` in `VisualRegressionTracker/VisualRegressionTracker.csproj` and merge it to `main`.
2. Push a tag with the same version, e.g. `git tag 5.0.0 && git push origin 5.0.0`.

The `Release` workflow then tests and packs the SDK, publishes it to NuGet through
[trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing)
and creates the GitHub release. It needs a trusted publishing policy on nuget.org for
this repository, workflow `release.yml` and environment `nuget`, and the
`NUGET_USER` secret holding the nuget.org user name that owns the policy.
