#!/usr/bin/env bash
set -euo pipefail

cd -- "$(dirname -- "${BASH_SOURCE[0]}")"

# Where stored runs live.
#
# NOT inside the publish output, which is what the default (AppContext.BaseDirectory/runstore)
# gives: this script publishes into AgroServer/publish/, so the store would sit in a directory that
# every rebuild rewrites and that "delete my build output" removes - taking the simulation results
# with it, at tens of gigabytes a run. Beside the repo instead, where it survives a rebuild and
# reads as data rather than as build output. Override it to put the store on another volume.
export AGRO_RunStoreRoot="${AGRO_RunStoreRoot:-$PWD/runstore}"

#dotnet clean
dotnet publish AgroServer/AgroServer.csproj --configuration Release -m:1 -v:q -clp:ErrorsOnly -p:Platform=x64 --framework=net10.0 --nologo --runtime linux-x64 -p:DebugType=None -p:DebugSymbols=false -p:PublishAot=true -p:OptimizationPreference=Speed --output AgroServer/publish/
exec ./AgroServer/publish/AgroServer "$@"
