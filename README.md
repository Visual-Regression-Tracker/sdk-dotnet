# .Net SDK for [Visual Regression Tracker](https://github.com/Visual-Regression-Tracker/Visual-Regression-Tracker)

[![NuGet version](https://buildstats.info/nuget/VisualRegressionTracker)](https://www.nuget.org/packages/VisualRegressionTracker)
[![Codacy Badge](https://app.codacy.com/project/badge/Grade/0b98190cea064d6f9f1210da653ba37b)](https://www.codacy.com/gh/Visual-Regression-Tracker/sdk-dotnet?utm_source=github.com&amp;utm_medium=referral&amp;utm_content=Visual-Regression-Tracker/sdk-dotnet&amp;utm_campaign=Badge_Grade)
[![Codacy Badge](https://app.codacy.com/project/badge/Coverage/0b98190cea064d6f9f1210da653ba37b)](https://www.codacy.com/gh/Visual-Regression-Tracker/sdk-dotnet?utm_source=github.com&utm_medium=referral&utm_content=Visual-Regression-Tracker/sdk-dotnet&utm_campaign=Badge_Coverage)

## Install

```csharp
dotnet package add VisualRegressionTracker
```

## Usage

### Import

```csharp
using VisualRegressionTracker;
```

### Configure connection

#### As code
```csharp
config = new Config(
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
);

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

    // Base64 encoded string
    image,

    // Allowed mismatch tollerance in %
    // Default: 0%
    diffTollerancePercent: 1,

    // Optional
    os: "Mac",
    browser: "Chrome",
    viewport: "800x600",
    device: "PC",

    // Array of areas to be ignored
    ignoreAreas: new [] {
        IgnoreArea(
            // X-coordinate relative of left upper corner
            10,
            // Y-coordinate relative of left upper corner
            20,
            // Area width in px
            300,
            // Height width in px
            400
        )
    },
);
```

### Example with Microsoft Playwright

#### Imports
```csharp
using PlaywrightSharp;
using VisualRegressionTracker;
```

#### Capture a screenshot using Playwright
```csharp
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync();
var page = await browser.NewPageAsync();
await page.GoToAsync("https://github.com/Visual-Regression-Tracker/sdk-dotnet/tree/main");
var screenshot = await page.ScreenshotAsync();
```

#### Track changes using Visual Regression Tracker
```csharp
var vrt = new VisualRegressionTracker.VisualRegressionTracker();
await using var cleanup = await vrt.Start();
await vrt.Track("sdk-dotnet", screenshot);
```
