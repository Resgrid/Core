REM Disables the AppPool timeout for IIS
REM Good for lower-traffic web apps that experience high initial latency issues between periods of low inactivity

%windir%\system32\inetsrv\appcmd set config -section:applicationPools -applicationPoolDefaults.processModel.idleTimeout:00:00:00 >> log.txt 2>> err.txt