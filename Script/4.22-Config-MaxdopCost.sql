set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED

select * from sys.configurations 
where name = 'cost threshold for parallelism'