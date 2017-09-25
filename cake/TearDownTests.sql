IF EXISTS (SELECT loginname
						FROM master.dbo.syslogins
						WHERE name='ndomain' AND dbname='NDomain')
BEGIN
	DROP LOGIN ndomain
END
