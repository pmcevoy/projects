USE [master]
RESTORE DATABASE [ModernArk] 
FROM  DISK = N'/backup/ModernArk.bak' 
	WITH  FILE = 1,  
	MOVE N'ModernArk' TO N'/var/opt/mssql/data/ModernArk.mdf',  
	MOVE N'ftrow_ModernArkSearchCatalog' TO N'/var/opt/mssql/data/ftrow_ModernArkSearchCatalog.ndf',  
	MOVE N'ModernArk_log' TO N'/var/opt/mssql/data/ModernArk.ldf',  
	NOUNLOAD,  
	REPLACE,  
	STATS = 5
GO
