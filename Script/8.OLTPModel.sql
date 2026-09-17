SET NOCOUNT ON
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED
declare @totalio bigint
declare @totalbytes bigint
declare @totalstall bigint

select 
			@TotalIO    = sum(NumberReads + NumberWrites),
			@TotalBytes = sum(BytesRead + BytesWritten),
			@TotalStall = sum(IoStallMS)
from		
			::fn_virtualfilestats(null, null)


select 
		[DbName] = db_name([DbId]),
		--[DbId],
		--[FileId],
		(select filename from sysaltfiles where fileid = f.FileId and dbid = f.DbId) as [FileName],
		--[NumberReads],  
		--[NumberWrites], 
		[BytesRead],    
		[BytesWritten], 
		--[IoStallMS],    
		--[TotalIO] = cast((NumberReads + NumberWrites) as bigint), 
		--[TotalBytes] = (BytesRead + BytesWritten),                
		--[AvgStallPerIO] = (1.0 * [IoStallMS] / ([NumberReads] + [NumberWrites] + 1)),            
		--[AvgBytesPerIO] = (1.0 * (BytesRead + BytesWritten) / (NumberReads + NumberWrites + 1)), 
		--[IO_percent] = (100.0 * (NumberReads + NumberWrites) / @TotalIO)                        
		  [Reads] = (100.0 * (NumberReads) / @TotalIO)    ,
		  [Writes] = (100.0 * (NumberWrites) / @TotalIO)      
into #r from 
fn_virtualfilestats(null, null)  as f
--for xml, ELEMENTS, ROOT('FileStats')
order by 3 desc

select * from #r


 drop table #r

