SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED
set nocount on

declare @versionsql INT

select @versionsql = 
CONVERT(INT,left(convert(CHAR(2),SERVERPROPERTY('ProductVersion')),2))


create table #configserver (propiedad varchar(255),
                            valor varchar(500),
							Estado bit -- 1 ok 0 mal
						   )

create table #msver (i bigint,
                     name varchar(255),
                     internalvalue varchar(255),
					 charactervalue varchar(255)
)

insert into #msver 
exec master..xp_msver

select v.name,v.charactervalue   
from #msver v 
where name = 'platform'


--drop table  #configserver
-- drop table #msver
-- drop table #SqlLogs