use master
go
-- get heap table
set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED

SELECT 
top 1
 convert(varchar(512),'TriggerdbHeader') as dbname,
       o.name, 
	   i.type_desc as index_type, 
	   o.type_desc as object_type, 
	   o.create_date,
	   p.rows 
into #result 
FROM sys.indexes i
INNER JOIN sys.objects o
 ON  i.object_id = o.object_id
INNER JOIN sys.partitions p 
ON i.object_id = p.OBJECT_ID AND i.index_id = p.index_id

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
  o.name, 
  i.type_desc as index_type, 
  o.type_desc as object_type, 
  o.create_date,
  p.rows 
	FROM sys.indexes i
	INNER JOIN sys.objects o
	ON  i.object_id = o.object_id
	INNER JOIN sys.partitions p 
	ON i.object_id = p.OBJECT_ID AND i.index_id = p.index_id
	WHERE o.type_desc = ''USER_TABLE''
	AND i.type_desc = ''HEAP''
	ORDER BY o.name
  '
 INSERT INTO #Result 
 EXEC (@SQL)

FETCH NEXT FROM BASES
INTO @DBNAME

END

CLOSE BASES
DEALLOCATE BASES

SELECT * FROM #Result 
order by dbname,rows desc
DROP TABLE #Result 







