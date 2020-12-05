#! /usr/bin/env bash

rm -rf tests/*/TestResults
dotnet test --collect:"XPlat Code Coverage"
dotnet reportgenerator "-reports:tests/**/coverage.cobertura.xml" -targetdir:htmlcov -verbosity:Warning
