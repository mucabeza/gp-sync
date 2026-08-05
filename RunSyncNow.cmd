@echo off
REM Manual sync run. Runs the sync with no arguments, so it ALWAYS synchronizes every
REM active sync data setting - it does not skip anything already synced today.
REM
REM Do not use "schtasks /Run /TN \MaxSfGpSync" for a manual run: the scheduled task
REM passes --scheduled, so it skips whatever already synced successfully today.
setlocal
pushd "%~dp0"
"%~dp0SalesforceDynamicsGpIntegration.exe"
set SYNC_EXIT=%ERRORLEVEL%
popd
echo.
echo Exit code: %SYNC_EXIT%   (0 = completed, 1 = at least one failure - check the logs folder)
exit /b %SYNC_EXIT%
