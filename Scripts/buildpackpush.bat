@ECHO OFF
SETLOCAL
SET VERSION=%1
SET NUGET=.\..\.nuget\nuget.exe

CALL buildpack %VERSION%
%NUGET% push .\..\Release\Mapsui.%VERSION%.nupkg 


