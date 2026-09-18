#!/bin/sh
set -eu
# Shares only the isolated evaluation database's network namespace.
export EVALUATION_POSTGRES="Host=localhost;Database=maintenance_evaluation;Username=postgres;GSS Encryption Mode=Disable;Password=$(cat /db-secret/password)"
exec dotnet IndustrialCopilot.Evaluation.dll "$@"
