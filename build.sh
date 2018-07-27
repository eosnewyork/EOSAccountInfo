#!/bin/sh -x
cd EOSDotNet
./build.sh
cd ..
dotnet build
dotnet publish -c release -r ubuntu.14.04-x64
