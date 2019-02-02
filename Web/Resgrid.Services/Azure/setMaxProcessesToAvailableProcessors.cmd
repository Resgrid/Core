REM Count the total number of available processors on this system
powershell -c "exit [System.Environment]::ProcessorCount"

REM set the default number of processes for our app pools in IIS equal to the number of available processors
%windir%\system32\inetsrv\appcmd set config -section:applicationPools -applicationPoolDefaults.processModel.maxProcesses:%ErrorLevel%