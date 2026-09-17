/* Get SP Get Recompìle */

use tempdb;
set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;



select top (1) convert(varchar(512),'TriggerdbHeader') database_name ,
       object_name(object_id) as objeto,
	   is_recompiled 
into #result
from sys.all_sql_modules
--where is_recompiled = 1

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
  select db_name() database_name ,
       object_name(object_id) as objeto,
	   is_recompiled 

from sys.all_sql_modules
where is_recompiled = 1

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




