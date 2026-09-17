-- Statistics Update
set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED

USE master 
	SELECT top 1
	convert(varchar(512),'TriggerdbHeader') as dbname,
	SCHEMA_NAME(o.Schema_ID) + N'.' + o.[NAME] AS [Object Name], o.[type_desc] AS [Object Type],
      i.[name] AS [Index Name], STATS_DATE(i.[object_id], i.index_id) AS [Statistics Date], 
      s.auto_created, s.no_recompute, s.user_created, s.is_incremental, s.is_temporary,
	  st.row_count, st.used_page_count
	into #result
	FROM sys.objects AS o WITH (NOLOCK)
	INNER JOIN sys.indexes AS i WITH (NOLOCK)
	ON o.[object_id] = i.[object_id]
	INNER JOIN sys.stats AS s WITH (NOLOCK)
	ON i.[object_id] = s.[object_id] 
	AND i.index_id = s.stats_id
	INNER JOIN sys.dm_db_partition_stats AS st WITH (NOLOCK)
	ON o.[object_id] = st.[object_id]
	AND i.[index_id] = st.[index_id]
	ORDER BY STATS_DATE(i.[object_id], i.index_id) DESC OPTION (RECOMPILE);

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
 
 '	
  SELECT 
   db_name() as dbname,
   SCHEMA_NAME(o.Schema_ID) + N''.'' + o.[NAME] AS [Object Name], o.[type_desc] AS [Object Type],
      i.[name] AS [Index Name], STATS_DATE(i.[object_id], i.index_id) AS [Statistics Date], 
      s.auto_created, s.no_recompute, s.user_created, s.is_incremental, s.is_temporary,
	  st.row_count, st.used_page_count
FROM sys.objects AS o WITH (NOLOCK)
INNER JOIN sys.indexes AS i WITH (NOLOCK)
ON o.[object_id] = i.[object_id]
INNER JOIN sys.stats AS s WITH (NOLOCK)
ON i.[object_id] = s.[object_id] 
AND i.index_id = s.stats_id
INNER JOIN sys.dm_db_partition_stats AS st WITH (NOLOCK)
ON o.[object_id] = st.[object_id]
AND i.[index_id] = st.[index_id]
WHERE o.[type] IN (''U'', ''V'')
AND st.row_count > 0
ORDER BY STATS_DATE(i.[object_id], i.index_id) DESC OPTION (RECOMPILE);
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