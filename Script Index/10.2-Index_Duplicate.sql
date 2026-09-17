set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED

DECLARE @DBName [nvarchar] (128) 
,@RowID [int] 
,@LoopStatus [int] 
,@DML varchar(max) 
 
SET @RowID=1 
SET @LoopStatus=1 
 
DECLARE @DuplicateIndexFind TABLE 
( 
[table] [nvarchar](257) NULL, 
[index] [sysname] NULL, 
[exactduplicate] [sysname] NULL, 
[DbName] varchar(100) NULL 
) 
 
DECLARE @DatabaseList TABLE ([RowNo] [smallint] identity (1, 1) 
,[DBName] [varchar](200)) 
 
INSERT INTO @DatabaseList 
SELECT '['+[name]+']' FROM [master].[sys].[databases] WITH (NOLOCK) 
WHERE [state_desc] = 'ONLINE' 
AND [source_database_id] IS NULL 
AND [database_id] > 4 
 
WHILE @LoopStatus<>0 
BEGIN 
SELECT @DBName = [DBName] 
FROM @DatabaseList WHERE [RowNo] = @RowID 
IF @@ROWCOUNT=0 
BEGIN 
SET @LoopStatus=0 
END 
ELSE 
BEGIN 
SET @DML='USE '+ @DBName +CHAR(13)+';'+ ' 
with indexcols as 
( 
select object_id as id, index_id as indid, name, 
( 
select case keyno when 0 then NULL else colid end as [data()] 
from '+ @DBName +'.sys.sysindexkeys' +' as k 
where k.id = i.object_id 
and k.indid = i.index_id 
order by keyno, colid 
for xml path('''')) as cols, 
( 
select case keyno when 0 then colid else NULL end as [data()] 
from '+ @DBName +'.sys.sysindexkeys' +' as k 
where k.id = i.object_id 
and k.indid = i.index_id 
order by colid 
for xml path('''') 
) as inc 
from '+ @DBName +'.sys.indexes as i) 
select 
object_schema_name(c1.id) + ''.'' + object_name(c1.id) as ''table'', 
c1.name as ''index'', 
c2.name as ''exactduplicate'' 
from indexcols as c1 
join indexcols as c2 
on c1.id = c2.id 
and c1.indid < c2.indid 
and c1.cols = c2.cols 
and c1.inc = c2.inc 
order by object_schema_name(c1.id) + ''.'' + object_name(c1.id)' 
 
INSERT INTO @DuplicateIndexFind([table] ,[index],[exactduplicate]) exec (@DML) 
 
update @DuplicateIndexFind 
set DbName=@DBName 
where DbName is NULL 
 
SET @RowID=@RowID+1 
END 
END 
 

select @@Servername ServerName,DbName DatabaseName,[table],[index],exactduplicate from @DuplicateIndexFind 
