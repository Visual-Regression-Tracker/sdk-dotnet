#! /usr/bin/env bash

rm -rf TestResults
dotnet test --coverlet --coverlet-output-format cobertura --results-directory "$PWD/TestResults"
dotnet reportgenerator "-reports:TestResults/coverage.cobertura.*.xml" -targetdir:htmlcov -verbosity:Warning

