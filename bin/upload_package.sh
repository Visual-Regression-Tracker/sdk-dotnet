#! /usr/bin/env bash

rm -f VisualRegressionTracker/bin/Release/*.nupkg
dotnet build --force --configuration Release

dotnet nuget push \
    VisualRegressionTracker/bin/Release/*.nupkg \
    --api-key $(cat nuget_key.txt) \
    --source https://api.nuget.org/v3/index.json
