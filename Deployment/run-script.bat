@echo off
REM Call Deploy-Resources function from MyScript.ps1 using PowerShell 7

pwsh.exe -NoProfile -ExecutionPolicy Bypass -Command ^
  "& { . '%~dp0Deploy-Resources.ps1'; Deploy-Resources }"
pause