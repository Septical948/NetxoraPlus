set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED




declare @wiattimetotal bigint
declare @CXPACKET bigint
declare @cxpacketpercent decimal(18,4)

 select @CXPACKET = wait_time_ms, @wiattimetotal  =  (select sum(wait_time_ms)  
                                from sys.dm_os_wait_stats
                      ) 
from sys.dm_os_wait_stats
 where wait_type = 'CXPACKET' 

set @cxpacketpercent = (@CXPACKET  * 1.00000) / (@wiattimetotal * 1.00000)
set @cxpacketpercent = @cxpacketpercent * 100.00


select case
         when cpu_count / hyperthread_ratio > 8 then 8
         else cpu_count / hyperthread_ratio
         end as optimal_maxdop_setting,
		  (
		   select value from sys.configurations
           where name = 'max degree of parallelism' 
		  ) as value_Actual,
		  @cxpacketpercent as 'Porcentaje CXPacket' 

from sys.dm_os_sys_info
