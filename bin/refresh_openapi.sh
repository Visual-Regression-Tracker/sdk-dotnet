#! /usr/bin/env bash

cd VisualRegressionTracker
dotnet dotnet-openapi refresh http://localhost:4200/api-json

# Hack fix for: https://github.com/Visual-Regression-Tracker/Visual-Regression-Tracker/issues/165
sed -ir 's/responses":{"undefined/responses":{"200/g' Api.json
rm Api.jsonr
