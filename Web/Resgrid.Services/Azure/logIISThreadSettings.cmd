REM Inspects the current processModel element of machine.config and logs to an output file
 
%windir%\system32\inetsrv\appcmd list config /commit:MACHINE -section:processModel >> log.txt 2>> err.txt
%windir%\system32\inetsrv\appcmd list config /commit:MACHINE -section:connectionManagement >> log.txt 2>> err.txt
