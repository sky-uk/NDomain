xcopy ..\..\source\NDomain.Sql\bin\Release\NDomain.Sql.dll lib\net45\ /y

NuGet.exe pack NDomain.Sql.nuspec -exclude *.cmd -OutputDirectory ..\