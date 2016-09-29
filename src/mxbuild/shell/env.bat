@echo off

:: visual studio environment
call %~dp0vcvarsallShim.bat

:: check for git
call where git.exe  > nul 2>&1
if %errorlevel% neq 0 (echo Please add 'git.exe' to path.)

:: check for iOS build host
if "%iosServerAddress%"=="" (echo Please set 'iosServerAddress' to enable building iOS apps.)
if "%iosServerPassword%"=="" (echo Please set 'iosServerPassword' to enable building iOS apps.)
if "%iosServerUser%"=="" (echo Please set 'iosServerUser' to enable building iOS apps.)

:: variables
set platform=

:: msbuild environment
set msbuildVar=%msbuildVar%RootDir;SrcDir;RefDir;
set msbuildVar=%msbuildVar%BuildDir;BuildBinDir;BuildTempDir;
set msbuildVar=%msbuildVar%DlsDir;MxbuildDir;DropDir;ToolsDir;
set msbuildVar=%msbuildVar%PkgDir;
set msbuildVar=%msbuildVar%ShimProj;PublishProj;IdProj;
msbuild %~dp0shell.proj /nologo /v:m /p:property=^"%msbuildVar%^" > init.bat
call init.bat
erase init.bat

:: path
set path=%path%;%envDir%
set path=%path%;%ProgramFiles(x86)%\MSBuild\Xamarin\Android
set path=%path%;%ProgramFiles(x86)%\Windows Kits\10\Debuggers\x64
set path=%path%;%ProgramFiles(x86)%\Android\android-sdk\platform-tools
set path=%path%;%ProgramFiles(x86)%\Android\android-sdk\tools
set path=%path%;%ProgramFiles(x86)%\Microsoft Emulator Manager\1.0
set path=%path%;%ProgramFiles%\Perforce
set path=%path%;%packagesDir%NUnit.Runners.2.6.4\tools\

:: alises
set doskey=%~dp0doskey.txt
doskey /macrofile=%doskey%
if exist %rootDir%doskey.txt doskey /macrofile=%rootDir%doskey.txt
color 1f

:: prompt
prompt $p $g

:: title
title %rootDir%

:: use unicode
REM chcp 65001 >NUL
