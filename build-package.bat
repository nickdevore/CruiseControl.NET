@echo off
cls
Tools\NAnt\NAnt.exe package -buildfile:ccnet.build -D:CCNetLabel=1.5.0.0 -nologo -logfile:nant-build-package.log.txt %*
echo %time% %date%
pause