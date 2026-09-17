set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED
 
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

insert into #configserver 
values ('ServerName',convert(varchar(500),
SERVERPROPERTY('ServerName')),1)

insert into #configserver 
values ('InstanceName',isnull( convert(varchar(500),
SERVERPROPERTY('InstanceName')),'Local'),1)

insert into #configserver 
values ('Collation',convert(varchar(500),SERVERPROPERTY('Collation')),1)

insert into #configserver 
values ('Edition',convert(varchar(500),SERVERPROPERTY('Edition')),
case when SERVERPROPERTY('Edition') 
in ('Developer Edition','Enterprise Evaluation Edition') 
then 0 else 1 end   )

insert into #configserver 
values ('IsClustered',convert(varchar(500),SERVERPROPERTY('IsClustered')),1)

insert into #configserver 
values ('IsIntegratedSecurityOnly',
convert(varchar(500),SERVERPROPERTY('IsIntegratedSecurityOnly')),
case when SERVERPROPERTY('IsIntegratedSecurityOnly') = 1 then 1 else 0 end )

insert into #configserver 
values ('ProductVersion',convert(varchar(500),SERVERPROPERTY('ProductVersion')),1)

insert into #configserver 
values ('ProductLevel',
convert(varchar(500),SERVERPROPERTY('ProductLevel')) ,1)


insert into #configserver 
select 'Cpu_count', cpu_count,1
  from sys.dm_os_sys_info

insert into #configserver 
select 'hyperthread_ratio', hyperthread_ratio,1   from sys.dm_os_sys_info

insert into #configserver 
select 'sqlserver_start_time', sqlserver_start_time,1    from sys.dm_os_sys_info

insert into #configserver 
select 'Platform ', charactervalue,1 
from #msver where name = 'Platform'


--- si es sql 2012 o superior
IF @versionsql >= 11 
BEGIN
	
	insert into #configserver 
	EXEC ('select ''Physical_memory_mb'', physical_memory_kb /1024 , 1   
	from sys.dm_os_sys_info')

	insert into #configserver 
	EXEC ('select ''Virtual_memory_mb'', virtual_memory_kb  /1024 , 1   
	from sys.dm_os_sys_info')
	
	insert into #configserver 
	EXEC ('select ''Windows_release'', windows_release  ,1    
	from sys.dm_os_windows_info')

	insert into #configserver 
	EXEC ('select ''windows_service_pack_level'', windows_service_pack_level , 1      
	from sys.dm_os_windows_info')
END
ELSE -- NO SQL 2012 O SUP
BEGIN
 	insert into #configserver 
	EXEC ('select ''Physical_memory_mb'', physical_memory_in_bytes 
            /1024 / 1024.00 , 1   
	from sys.dm_os_sys_info')

	insert into #configserver 
	EXEC ('select ''Virtual_memory_mb'', virtual_memory_in_bytes
     /1024  / 1024 , 1 
	from sys.dm_os_sys_info')
END

----------------------------- INSTAT FILE INIT

 
USE MASTER;
SET NOCOUNT ON

-- *** WARNING: Undocumented commands used in this script !!! *** --

--Exit if a database named DummyTestDB exists
IF DB_ID('DummyTestDB') IS NOT NULL
BEGIN
  RAISERROR('A database named DummyTestDB already exists, exiting script', 20, 1) WITH LOG
END

--Temptable to hold output from sp_readerrorlog
IF OBJECT_ID('tempdb..#SqlLogs') IS NOT NULL DROP TABLE #SqlLogs
GO
CREATE TABLE #SqlLogs(LogDate datetime2(0), ProcessInfo VARCHAR(20), TEXT VARCHAR(MAX))

--Turn on trace flags 3004 and 3605
DBCC TRACEON(3004, 3605, -1) WITH NO_INFOMSGS

--Create a dummy database to see the output in the SQL Server Errorlog
CREATE DATABASE DummyTestDB 
GO

--Turn off trace flags 3004 and 3605
DBCC TRACEOFF(3004, 3605, -1) WITH NO_INFOMSGS

--Remove the DummyDB
DROP DATABASE DummyTestDB;

--Now go check the output in the SQL Server Error Log File
--This can take a while if you have a large errorlog file
INSERT INTO #SqlLogs(LogDate, ProcessInfo, TEXT)
EXEC sp_readerrorlog 0, 1, 'Zeroing'

IF EXISTS(
           SELECT * FROM #SqlLogs
           WHERE TEXT LIKE 'Zeroing completed%'
            AND TEXT LIKE '%DummyTestDB.mdf%'
            AND LogDate > DATEADD(HOUR, -1, LogDate)
        )
   BEGIN
    insert into #configserver 
    select ' instant file initialization', 'False' , 0
	
	
   END
ELSE
   BEGIN
    insert into #configserver 
    select ' instant file initialization', 'True' , 1
	
   END

------------- Config Memory ------------------
insert into  #configserver
select name,convert(varchar(20),value_in_use),1 
from sys.configurations 
where name like '%memory%' or
name like '%fill factor%' or
name like '%priority boost%' 
or name like '%lightweight pooling%'
or name like '%remote login timeout (s)%'
or name like '%remote query timeout (s)%'
or name like '%cost threshold for parallelism%'
or name like '%max degree of parallelism%'
or name like '%clr enabled%'
or name like '%backup compression default%'
or name like '%xp_cmdshell%'







--select * 
--from #configserver 

select propiedad, valor 
from #configserver
where propiedad like 'ServerName' or propiedad like 'Instancename' or propiedad like 'Collation' 
or propiedad like 'Cpu_Count' or propiedad like 'Tipo Servidor' or propiedad like 'Physical_memory_mb'

/*
drop table #configserver
drop table #msver
drop table #SqlLogs
*/