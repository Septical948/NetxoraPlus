SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED
set nocount on

select * 
from sys.configurations 
where name ='remote query timeout (s)'