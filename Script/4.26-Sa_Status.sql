set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED

select name,is_disabled  
from sys.sql_logins
where name = 'sa' 