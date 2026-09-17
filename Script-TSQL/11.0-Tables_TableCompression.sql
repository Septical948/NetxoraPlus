-- Get Table Compression
set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED

USE master 
	SELECT top 1
	convert(varchar(512),'TriggerdbHeader') as dbname,
	SCHEMA_NAME(o.Schema_ID) AS [Schema Name], OBJECT_NAME(p.object_id) AS [ObjectName], 
	SUM(p.Rows) AS [RowCount], p.data_compression_desc AS [Compression Type]
	into #result
	FROM sys.partitions AS p WITH (NOLOCK)
	INNER JOIN sys.objects AS o WITH (NOLOCK)
	ON p.object_id = o.object_id
	GROUP BY  SCHEMA_NAME(o.Schema_ID), p.object_id, data_compression_desc
	ORDER BY SUM(p.Rows) DESC OPTION (RECOMPILE);


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
 SELECT db_name() as dbname, 
 SCHEMA_NAME(o.Schema_ID) AS [Schema Name], OBJECT_NAME(p.object_id) AS [ObjectName], 
 SUM(p.Rows) AS [RowCount], p.data_compression_desc AS [Compression Type]
 FROM sys.partitions AS p WITH (NOLOCK)
 INNER JOIN sys.objects AS o WITH (NOLOCK)
 ON p.object_id = o.object_id
 WHERE index_id < 2 --ignore the partitions from the non-clustered index if any
AND OBJECT_NAME(p.object_id) NOT LIKE N''sys%''
AND OBJECT_NAME(p.object_id) NOT LIKE N''spt_%''
AND OBJECT_NAME(p.object_id) NOT LIKE N''queue_%'' 
AND OBJECT_NAME(p.object_id) NOT LIKE N''filestream_tombstone%'' 
AND OBJECT_NAME(p.object_id) NOT LIKE N''fulltext%''
AND OBJECT_NAME(p.object_id) NOT LIKE N''ifts_comp_fragment%''
AND OBJECT_NAME(p.object_id) NOT LIKE N''filetable_updates%''
AND OBJECT_NAME(p.object_id) NOT LIKE N''xml_index_nodes%''
AND OBJECT_NAME(p.object_id) NOT LIKE N''sqlagent_job%''
AND OBJECT_NAME(p.object_id) NOT LIKE N''plan_persist%''
GROUP BY  SCHEMA_NAME(o.Schema_ID), p.object_id, data_compression_desc
ORDER BY SUM(p.Rows) DESC OPTION (RECOMPILE);
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