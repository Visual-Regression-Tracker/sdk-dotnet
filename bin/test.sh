#! /usr/bin/env bash

rm -rf TestResults
dotnet test --coverlet --coverlet-output-format cobertura --results-directory "$PWD/TestResults"
dotnet reportgenerator "-reports:TestResults/coverage.cobertura.*.xml" -targetdir:htmlcov -verbosity:Warning

# codacy reporter doesn't support globs, so copy it to known location
cp TestResults/coverage.cobertura.*.xml ./coverage.xml
