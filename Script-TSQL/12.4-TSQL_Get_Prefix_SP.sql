/* Get SP Prefix SP_ */

use master;
set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

select
top 1
convert(varchar(512),'Triggerdbheader') as dbname,
       SCHEMA_NAME(schema_id) as schema_name,
       name 
into #result
from sys.procedures p
--where name like 'sp_%'

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
convert(varchar(512),db_name()) as dbname,
       SCHEMA_NAME(schema_id) as schema_name,
       name 
from sys.procedures p
where name like ''sp[_]%''
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







