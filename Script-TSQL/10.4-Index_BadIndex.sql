-- Possible Bad NC Indexes (writes > reads)  (Query 61) (Bad NC Indexes)
set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED

USE master 


	SELECT TOP 1
	convert(varchar(512),'TriggerdbHeader') dbname,
	SCHEMA_NAME(o.[schema_id]) AS [Schema Name], 
	OBJECT_NAME(s.[object_id]) AS [Table Name],
	i.name AS [Index Name], i.index_id, 
	i.is_disabled, i.is_hypothetical, i.has_filter, i.fill_factor,
	s.user_updates AS [Total Writes], s.user_seeks + s.user_scans + s.user_lookups AS [Total Reads],
	s.user_updates - (s.user_seeks + s.user_scans + s.user_lookups) AS [Difference]
	INTO #Result	
	FROM sys.dm_db_index_usage_stats AS s WITH (NOLOCK)
	left JOIN sys.indexes AS i WITH (NOLOCK)
	ON s.[object_id] = i.[object_id]
	AND i.index_id = s.index_id
	left JOIN sys.objects AS o WITH (NOLOCK)
	ON i.[object_id] = o.[object_id]
	
DECLARE @SQL VARCHAR(4000)

DECLARE @DBNAME SYSNAME
DECLARE BASES CURSOR FOR
SELECT NAME FROM SYS.DATABASES WHERE 
[state_desc] = 'ONLINE' 
AND [source_database_id] IS NULL 
AND [database_id] > 4   AND DATABASEPROPERTYEX(name, 'UserAccess') <> 'SINGLE_USER' 

OPEN BASES;

FETCH NEXT FROM BASES
INTO @DBNAME

WHILE @@FETCH_STATUS = 0
BEGIN

SET @SQL = 
 'USE ' + QUOTENAME(@DBNAME)  +
 
 '	SELECT 	db_name() dbname,
	SCHEMA_NAME(o.[schema_id]) AS [Schema Name], 
	OBJECT_NAME(s.[object_id]) AS [Table Name],
	i.name AS [Index Name], i.index_id, 
	i.is_disabled, i.is_hypothetical, i.has_filter, i.fill_factor,
	s.user_updates AS [Total Writes], s.user_seeks + s.user_scans + s.user_lookups AS [Total Reads],
	s.user_updates - (s.user_seeks + s.user_scans + s.user_lookups) AS [Difference]
	FROM sys.dm_db_index_usage_stats AS s WITH (NOLOCK)
	INNER JOIN sys.indexes AS i WITH (NOLOCK)
	ON s.[object_id] = i.[object_id]
	AND i.index_id = s.index_id
	INNER JOIN sys.objects AS o WITH (NOLOCK)
	ON i.[object_id] = o.[object_id]
	WHERE OBJECTPROPERTY(s.[object_id],''IsUserTable'') = 1
	AND s.database_id = DB_ID()
	AND s.user_updates > (s.user_seeks + s.user_scans + s.user_lookups)
	AND i.index_id > 1 AND i.[type_desc] = N''NONCLUSTERED''
	AND i.is_primary_key = 0 AND i.is_unique_constraint = 0 AND i.is_unique = 0
	ORDER BY [Difference] DESC, [Total Writes] DESC, [Total Reads] ASC OPTION (RECOMPILE);
 '
 INSERT INTO #Result 
 EXEC (@SQL)

FETCH NEXT FROM BASES
INTO @DBNAME

END

CLOSE BASES
DEALLOCATE BASES

SELECT * FROM #Result 
DROP TABLE #Result 