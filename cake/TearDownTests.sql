IF EXISTS (SELECT loginname
						FROM master.dbo.syslogins
						WHERE name='%_USER_NAME_%' AND dbname='%_EVENT_STORE_DB_%')
BEGIN
	DROP LOGIN %_USER_NAME_%
END
