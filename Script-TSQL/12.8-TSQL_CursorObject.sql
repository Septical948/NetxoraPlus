/* Get SP Cursores */

use tempdb;
set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;


select  top 1
CONVERT(varchar(512),'triggerdbheader') as dbname,
SCHEMA_NAME(o.schema_id) as schemaname, 
o.name as objectname
into #result
from sys.all_sql_modules m
left join sys.objects  o
on 
m.object_id = o.object_id 





DECLARE @SQL VARCHAR(4000)

DECLARE @DBNAME SYSNAME
DECLARE BASES CURSOR FOR
SELECT NAME FROM SYS.DATABASES WHERE 
[state_desc] = 'ONLINE' 
AND [source_database_id] IS NULL 
AND [database_id] > 4   AND 
DATABASEPROPERTYEX(name, 'UserAccess') <> 'SINGLE_USER' 

OPEN BASES;

FETCH NEXT FROM BASES
INTO @DBNAME

WHILE @@FETCH_STATUS = 0
BEGIN

SET @SQL = 
 'USE ' + QUOTENAME(@DBNAME)  +
 '
  select 
db_name() as dbname,
SCHEMA_NAME(o.schema_id) as schemaname, 
o.name as objectname
from sys.all_sql_modules m
inner join sys.objects  o
on 
m.object_id = o.object_id 
where definition like ''%CURSOR FOR%''
and o.type <> ''S''

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




