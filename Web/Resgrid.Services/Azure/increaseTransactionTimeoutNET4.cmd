REM increases the default maximum transaction timeout in Azure machine
REM this script modifies the Machine.config for .NET 4.0 applications
REM please note that appcmd is present in both web and worker roles

%windir%\system32\inetsrv\appcmd set config /commit:MACHINE /clr:4 -section:machineSettings -maxTimeout:23:00:00 >> log.txt 2>> err.txt