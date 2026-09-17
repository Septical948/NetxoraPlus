-- get Disable Index
use master
set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED

SELECT 
top 1
convert(varchar(512),'TriggerdbHeader') as dbname,
i.name AS Index_Name, i.index_id, 
i.type_desc, s.name AS 'Schema_Name', 
o.name AS Table_Name
into #result
FROM sys.indexes i
JOIN sys.objects o on o.object_id = i.object_id
JOIN sys.schemas s on s.schema_id = o.schema_id
ORDER BY
i.name

DECLARE @SQL VARCHAR(4000)

DECLARE @DBNAME SYSNAME
DECLARE BASES CURSOR FOR
SELECT NAME FROM sys.databases WHERE 
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
	i.name AS Index_Name, i.index_id, 
	i.type_desc, s.name AS ''Schema_Name'', 
	o.name AS Table_Name
	FROM sys.indexes i
	JOIN sys.objects o on o.object_id = i.object_id
	JOIN sys.schemas s on s.schema_id = o.schema_id
	WHERE i.is_disabled = 1
	ORDER BY
	i.name
  '
 INSERT INTO #result 
 EXEC (@SQL)

FETCH NEXT FROM BASES
INTO @DBNAME

END

CLOSE BASES
DEALLOCATE BASES

SELECT * FROM #result 
DROP TABLE #result 







