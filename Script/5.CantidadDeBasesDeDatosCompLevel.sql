-- check db comp level <> SQL
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED
set nocount on


declare @cmp varchar(50)
select @cmp = [compatibility_level] 
from  [master].[sys].[databases] where name='master'


select name,compatibility_level,@cmp as desable   
from sys.databases 
where compatibility_level <> @cmp
and state = 0
union all
select 'TriggerdbHeader',compatibility_level,@cmp as desable   
from sys.databases 
where database_id = 1

