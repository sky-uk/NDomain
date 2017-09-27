xcopy ..\..\source\NDomain.Log4Net\bin\Release\NDomain.Log4Net.dll lib\net45\ /y

NuGet.exe pack NDomain.Log4Net.nuspec -exclude *.cmd -OutputDirectory ..\