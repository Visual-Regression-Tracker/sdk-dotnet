#! /usr/bin/env bash

rm -rf tests/*/TestResults
dotnet test --collect:"XPlat Code Coverage"
dotnet reportgenerator "-reports:tests/**/coverage.cobertura.xml" -targetdir:htmlcov -verbosity:Warning

# codacy reporter doesn't support globs, so copy it to knwon location
cp tests/VisualRegressionTracker.Tests/TestResults/*/coverage.cobertura.xml ./coverage.xml
