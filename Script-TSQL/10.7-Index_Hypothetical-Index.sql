use master
go

set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED

SELECT 
    top 1
	convert(varchar(512),'TriggerdbHeader') AS [DBName], 
    QUOTENAME(t.name) as tablename, 
	QUOTENAME(o.[name]) as objectname, i.name, 'INDEX' as type
	into #result
	FROM sys.indexes i 
	INNER JOIN sys.objects o ON o.[object_id] = i.[object_id] 
	INNER JOIN sys.tables AS mst ON mst.[object_id] = i.[object_id]
	INNER JOIN sys.schemas AS t ON t.[schema_id] = mst.[schema_id]
	
	
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



SET @sql = 'USE ' + QUOTENAME(@dbname) + ';

	SELECT ''' + REPLACE(@dbname, CHAR(39), CHAR(95)) + ''' AS [DBName], QUOTENAME(t.name), QUOTENAME(o.[name]), i.name, ''INDEX'' 
	FROM sys.indexes i 
	INNER JOIN sys.objects o ON o.[object_id] = i.[object_id] 
	INNER JOIN sys.tables AS mst ON mst.[object_id] = i.[object_id]
	INNER JOIN sys.schemas AS t ON t.[schema_id] = mst.[schema_id]
	WHERE i.is_hypothetical = 1
	UNION ALL
	SELECT ''' + REPLACE(@dbname, CHAR(39), CHAR(95)) + ''' AS [DBName], QUOTENAME(t.name), QUOTENAME(o.[name]), s.name, ''STATISTICS'' 
	FROM sys.stats s 
	INNER JOIN sys.objects o (NOLOCK) ON o.[object_id] = s.[object_id]
	INNER JOIN sys.tables AS mst (NOLOCK) ON mst.[object_id] = s.[object_id]
	INNER JOIN sys.schemas AS t (NOLOCK) ON t.[schema_id] = mst.[schema_id]
	WHERE (s.name LIKE ''hind_%'' OR s.name LIKE ''_dta_stat%'') AND auto_created = 0
	AND s.name NOT IN (SELECT name FROM ' + QUOTENAME(@dbname) + '.sys.indexes)'

INSERT INTO #Result 
 EXEC (@SQL)

FETCH NEXT FROM BASES
INTO @DBNAME

END

CLOSE BASES
DEALLOCATE BASES

SELECT * FROM #Result 
DROP TABLE #Result 