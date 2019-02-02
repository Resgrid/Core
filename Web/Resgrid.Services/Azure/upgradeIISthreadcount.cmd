REM Increases the number of available IIS threads for high performance applications
REM Uses the recommended values from http://msdn.microsoft.com/en-us/library/ms998549.aspx#scalenetchapt06_topic8
REM Values may be subject to change depending on your needs
REM Assumes you're running on two cores (medium instance on Azure)
 
%windir%\system32\inetsrv\appcmd set config /commit:MACHINE -section:processModel -maxWorkerThreads:100 >> log.txt 2>> err.txt
%windir%\system32\inetsrv\appcmd set config /commit:MACHINE -section:processModel -minWorkerThreads:50 >> log.txt 2>> err.txt
%windir%\system32\inetsrv\appcmd set config /commit:MACHINE -section:processModel -minIoThreads:50 >> log.txt 2>> err.txt
%windir%\system32\inetsrv\appcmd set config /commit:MACHINE -section:processModel -maxIoThreads:100 >> log.txt 2>> err.txt
 
REM Adjust the maximum number of connections per core for all IP addresses
%windir%\system32\inetsrv\appcmd set config /commit:MACHINE -section:connectionManagement /+["address='*',maxconnection='24'"] >> log.txt 2>> err.txt
