-- Exteneded events para query tuning

CREATE EVENT SESSION [TRIGGERDB_PERFFORMANCE] ON SERVER 
ADD EVENT sqlserver.rpc_completed(SET collect_data_stream=(1)
    ACTION(package0.event_sequence,sqlserver.client_app_name,sqlserver.client_hostname,sqlserver.client_pid,sqlserver.database_id,sqlserver.database_name,sqlserver.is_system,sqlserver.nt_username,sqlserver.query_hash,sqlserver.request_id,sqlserver.server_principal_name,sqlserver.session_id,sqlserver.session_nt_username,sqlserver.session_server_principal_name,sqlserver.sql_text,sqlserver.transaction_id,sqlserver.username)
	  WHERE ( ( duration >= ( 500000 ) )
            )),
ADD EVENT sqlserver.sql_batch_completed(SET collect_batch_text=(1)
    ACTION(package0.event_sequence,sqlserver.client_app_name,sqlserver.client_hostname,sqlserver.client_pid,sqlserver.database_id,sqlserver.database_name,sqlserver.is_system,sqlserver.nt_username,sqlserver.query_hash,sqlserver.request_id,sqlserver.server_principal_name,sqlserver.session_id,sqlserver.session_nt_username,sqlserver.session_server_principal_name,sqlserver.sql_text,sqlserver.transaction_id,sqlserver.username)
	  WHERE ( ( duration >= ( 500000 ) )
            )
	  )
ADD TARGET package0.event_file(SET filename=N'E:\triggerdb\monitor\TRDB-PERFORMANCE.xel',max_file_size=(8192),max_rollover_files=(2))
WITH (MAX_MEMORY=200800 KB,EVENT_RETENTION_MODE=ALLOW_SINGLE_EVENT_LOSS,
     MAX_DISPATCH_LATENCY=10 SECONDS,MAX_EVENT_SIZE=0 KB,
	 MEMORY_PARTITION_MODE=PER_CPU,TRACK_CAUSALITY=OFF,STARTUP_STATE=ON)
GO


