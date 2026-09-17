use tempdb;
set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

-- Get Lines Stores SP
with c as
(select 
       convert(varchar(512),'TriggerdbHeader') as dbname  ,
       [Schema]=schema_name(p.schema_id), 
       [Proc_Name]=p.name
     , Num_of_LineCode=(len(m.definition) -len(replace(m.definition,  char(0x0d) + char(0x0a), ''))) /2
from sys.sql_modules m
inner join sys.procedures p
on m.object_id = p.object_id )
select 
top 1 
* 
into #result 
from c 

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
 
 ';	
  with c as
(select 
       db_name() as dbname  ,
       [Schema]=schema_name(p.schema_id), 
       [Proc_Name]=p.name
     , Num_of_LineCode=(len(m.definition) -len(replace(m.definition,  char(0x0d) + char(0x0a), ''''))) /2
	from sys.sql_modules m
	inner join sys.procedures p
	on m.object_id = p.object_id )
	select 
	* 
    from c where Num_of_LineCode > 250
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
