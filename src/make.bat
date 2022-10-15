@echo off
rem cls
cd Affirma.ThreeSharp
call make.bat

cd ..

call make-config.bat

erase wins3fs.exe
erase wins3fs-log.txt
touch wins3fs-log.txt
csc /nologo /out:S3FS.dll /t:library /unsafe AlternativeStreams.cs AssemblyInfo.cs DeviceIO.cs S3FS.cs dpapi.cs /r:NeoGeo.Library.SMB.dll /r:NeoGeo.Library.SMB.Provider.dll /r:Affirma.ThreeSharp.Wrapper.dll /r:Affirma.ThreeSharp.dll 
if .%1 == .service goto :SERVICE else goto :NOSERVICE
:NOSERVICE
csc /nologo /out:wins3fs.exe /t:winexe /unsafe ServiceInstaller.cs Server.cs /r:NeoGeo.Library.SMB.dll /r:NeoGeo.Library.SMB.Provider.dll /win32icon:Simple.ico
goto :END
:SERVICE
csc /nologo /out:wins3fs.exe /t:winexe /unsafe /define:SERVICE ServiceInstaller.cs Server.cs /r:NeoGeo.Library.SMB.dll /r:NeoGeo.Library.SMB.Provider.dll 
:END
echo Done
