-- get index fill factor < 80
set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED

use tempdb
SELECT 
top 0
DB_NAME() AS Database_Name
, sc.name AS Schema_Name
, o.name AS Table_Name
, o.type_desc
, i.name AS Index_Name
, i.type_desc AS Index_Type
, i.fill_factor
into #result
FROM sys.indexes i
INNER JOIN sys.objects o ON i.object_id = o.object_id
INNER JOIN sys.schemas sc ON o.schema_id = sc.schema_id
WHERE i.name IS NOT NULL
AND o.type = 'U'
and i.fill_factor < 80 and i.fill_factor <> 0
ORDER BY i.fill_factor DESC, o.name


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
  DB_NAME() AS Database_Name
	, sc.name AS Schema_Name
	, o.name AS Table_Name
	, o.type_desc
	, i.name AS Index_Name
	, i.type_desc AS Index_Type
	, i.fill_factor
	FROM sys.indexes i
	INNER JOIN sys.objects o ON i.object_id = o.object_id
	INNER JOIN sys.schemas sc ON o.schema_id = sc.schema_id
	WHERE i.name IS NOT NULL
	AND o.type = ''U''
	and i.fill_factor < 80 and i.fill_factor <> 0
	ORDER BY i.fill_factor DESC, o.name
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




