#!/usr/bin/env bash
set -euo pipefail
dotnet restore NotaFiscalHub.sln
dotnet build NotaFiscalHub.sln --no-restore --configuration Release
dotnet test --no-build --configuration Release --filter "FullyQualifiedName~UnitTests|FullyQualifiedName~ArchitectureTests" --logger "trx;LogFileName=unit.trx" --collect:"XPlat Code Coverage"
dotnet test --no-build --configuration Release --filter "FullyQualifiedName~IntegrationTests" --logger "trx;LogFileName=integration.trx" --collect:"XPlat Code Coverage"
