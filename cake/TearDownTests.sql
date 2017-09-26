IF EXISTS (SELECT loginname
						FROM master.dbo.syslogins
						WHERE name='%_USER_NAME_%' AND dbname='%_EVENT_STORE_DB_%')
BEGIN
	PRINT N'Found a [%_USER_NAME_%] login. Removing it...'
	DROP LOGIN %_USER_NAME_%
END
