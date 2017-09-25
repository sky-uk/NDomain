-- Create login
IF NOT EXISTS (SELECT loginname
								FROM master.dbo.syslogins
								WHERE name='ndomain' AND dbname='NDomain')
BEGIN
	CREATE LOGIN [ndomain] WITH PASSWORD=N'', DEFAULT_DATABASE=[NDomain], DEFAULT_LANGUAGE=[us_english], CHECK_EXPIRATION=OFF, CHECK_POLICY=OFF
END

-- Create user associated with login
IF NOT EXISTS (SELECT name
								FROM sys.database_principals
								WHERE name = 'ndomain')
BEGIN
	CREATE USER [ndomain] FOR LOGIN [ndomain]
END

-- Create Agggregates and Events tables
IF NOT EXISTS (SELECT * FROM sys.tables WHERE object_id = OBJECT_ID(N'[Aggregates]'))
	BEGIN
	CREATE TABLE [Aggregates]
	(
			[aggregate_id] [nvarchar](255) NOT NULL,
			[aggregate_type] [nvarchar](256) NOT NULL,
			[aggregate_event_seq] [int] NOT NULL,
			[snapshot_event_seq] [int] NOT NULL,
		 CONSTRAINT [PK_Aggregates] PRIMARY KEY CLUSTERED
		(
			[aggregate_id] ASC
		)
		WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
	) ON [PRIMARY]
	END

	IF NOT EXISTS (SELECT * FROM sys.tables WHERE object_id = OBJECT_ID(N'[Events]'))
		BEGIN
		CREATE TABLE [Events]
		(
			[aggregate_id] [nvarchar](255) NOT NULL,
			[aggregate_type] [nvarchar](256) NOT NULL,
			[event_seq] [int] NOT NULL,
			[timestamp] [datetime] NOT NULL,
			[msg_type] [nvarchar](256) NOT NULL,
			[msg_ver] [smallint] NOT NULL,
			[msg_data] [nvarchar](max) NOT NULL,
			[committed] [bit] NOT NULL,
			[transaction_id] [nvarchar](256) NOT NULL,
		 CONSTRAINT [PK_Events] PRIMARY KEY CLUSTERED
		(
			[aggregate_id] ASC,
			[event_seq] ASC
		)
		WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
		) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
		END
