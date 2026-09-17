/*
Find Triggers
*/

set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED

use tempdb
go

select top 1 convert(varchar(512),'TriggerdbHeader') as database_name,
convert(varchar(512),'name') as triggername,
convert(varchar(512),'parent_id') as tabla
into #result 



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
	select db_name() as database_name,
	name as triggername,
	object_name(parent_id) as tabla
	from sys.triggers 
	where is_disabled = 0  '
 INSERT INTO #Result 
 EXEC (@SQL)

FETCH NEXT FROM BASES
INTO @DBNAME

END

CLOSE BASES
DEALLOCATE BASES

SELECT * FROM #Result 
DROP TABLE #Result 










