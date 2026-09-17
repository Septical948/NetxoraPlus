/*
Is committed Snapshot

*/
select
top 1
'Triggerdbheader' as dbname,
is_read_committed_snapshot_on 
from sys.databases 
union all
select name,is_read_committed_snapshot_on 
from sys.databases 
where is_read_committed_snapshot_on = 1