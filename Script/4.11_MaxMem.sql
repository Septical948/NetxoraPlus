SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED

SET NOCOUNT ON;
SET ANSI_WARNINGS ON;
SET QUOTED_IDENTIFIER ON;
SET DATEFORMAT mdy;

DECLARE @sqlcmd NVARCHAR(max), @params NVARCHAR(600)
DECLARE @sqlmajorver int, @sqlminorver int, @sqlbuild int
DECLARE @ErrorMessage NVARCHAR(4000)
DECLARE @masterpid int
DECLARE @permstbl TABLE ([name] sysname);
DECLARE @maxservermem bigint, @minservermem bigint, @systemmem bigint, @systemfreemem bigint, @numa int, @numa_nodes_afinned tinyint
DECLARE @commit_target bigint -- Includes stolen and reserved memory in the memory manager
DECLARE @committed bigint -- Does not include reserved memory in the memory manager
DECLARE @mwthreads_count int, @xtp int, @clustered bit, @arch smallint, @osver VARCHAR(5), @ostype VARCHAR(10), @SystemManufacturer VARCHAR(128)

SELECT @sqlmajorver = CONVERT(int, (@@microsoftversion / 0x1000000) & 0xff);
SELECT @masterpid = principal_id FROM master.sys.database_principals (NOLOCK) WHERE sid = SUSER_SID()

INSERT INTO @permstbl
SELECT a.name
FROM master.sys.all_objects a (NOLOCK) INNER JOIN master.sys.database_permissions b (NOLOCK) ON a.[OBJECT_ID] = b.major_id
WHERE a.type IN ('P', 'X') AND b.grantee_principal_id <>0
AND b.grantee_principal_id <>2
AND b.grantee_principal_id = @masterpid;

IF @sqlmajorver >= 14
BEGIN
	SET @sqlcmd = N'SELECT @ostypeOUT = host_platform, @archOUT = CASE WHEN @@VERSION LIKE ''%<X64>%'' THEN 64 WHEN @@VERSION LIKE ''%<IA64>%'' THEN 128 ELSE 32 END FROM sys.dm_os_host_info (NOLOCK)';
	SET @params = N'@ostypeOUT VARCHAR(10) OUTPUT, @archOUT smallint OUTPUT';
	EXECUTE sp_executesql @sqlcmd, @params, @ostypeOUT=@ostype OUTPUT, @archOUT=@arch OUTPUT;
END
ELSE
BEGIN
    DECLARE @sysinfo TABLE (id int, 
        [Name] NVARCHAR(256), 
        Internal_Value bigint, 
        Character_Value NVARCHAR(256));
        
    INSERT INTO @sysinfo
    EXEC xp_msver;

    SELECT @arch = CASE WHEN RTRIM(Character_Value) LIKE '%x64%' OR RTRIM(Character_Value) LIKE '%AMD64%' THEN 64
        WHEN RTRIM(Character_Value) LIKE '%x86%' OR RTRIM(Character_Value) LIKE '%32%' THEN 32
        WHEN RTRIM(Character_Value) LIKE '%IA64%' THEN 128 END
    FROM @sysinfo
    WHERE [Name] LIKE 'Platform%';

	SET @ostype = 'Windows'
END;

IF @sqlmajorver = 9
BEGIN
	SET @sqlcmd = N'SELECT @systemmemOUT = t1.record.value(''(./Record/MemoryRecord/TotalPhysicalMemory)[1]'', ''bigint'')/1024, 
	@systemfreememOUT = t1.record.value(''(./Record/MemoryRecord/AvailablePhysicalMemory)[1]'', ''bigint'')/1024
FROM (SELECT MAX([TIMESTAMP]) AS [TIMESTAMP], CONVERT(xml, record) AS record 
	FROM sys.dm_os_ring_buffers (NOLOCK)
	WHERE ring_buffer_type = N''RING_BUFFER_RESOURCE_MONITOR''
		AND record LIKE ''%RESOURCE_MEMPHYSICAL%''
	GROUP BY record) AS t1';
END
ELSE
BEGIN
	SET @sqlcmd = N'SELECT @systemmemOUT = total_physical_memory_kb/1024, @systemfreememOUT = available_physical_memory_kb/1024 FROM sys.dm_os_sys_memory';
END

SET @params = N'@systemmemOUT bigint OUTPUT, @systemfreememOUT bigint OUTPUT';

EXECUTE sp_executesql @sqlcmd, @params, @systemmemOUT=@systemmem OUTPUT, @systemfreememOUT=@systemfreemem OUTPUT;

IF @sqlmajorver >= 9 AND @sqlmajorver < 11
BEGIN
	SET @sqlcmd = N'SELECT @commit_targetOUT=bpool_commit_target*8, @committedOUT=bpool_committed*8 FROM sys.dm_os_sys_info (NOLOCK)'
END
ELSE IF @sqlmajorver >= 11
BEGIN
	SET @sqlcmd = N'SELECT @commit_targetOUT=committed_target_kb, @committedOUT=committed_kb FROM sys.dm_os_sys_info (NOLOCK)'
END

SET @params = N'@commit_targetOUT bigint OUTPUT, @committedOUT bigint OUTPUT';

EXECUTE sp_executesql @sqlcmd, @params, @commit_targetOUT=@commit_target OUTPUT, @committedOUT=@committed OUTPUT;

SELECT @minservermem = CONVERT(int, [value]) FROM sys.configurations (NOLOCK) WHERE [Name] = 'min server memory (MB)';
SELECT @maxservermem = CONVERT(int, [value]) FROM sys.configurations (NOLOCK) WHERE [Name] = 'max server memory (MB)';
SELECT @mwthreads_count = max_workers_count FROM sys.dm_os_sys_info;
SELECT @numa_nodes_afinned = COUNT (DISTINCT parent_node_id) FROM sys.dm_os_schedulers WHERE scheduler_id < 255 AND parent_node_id < 64 AND is_online = 1;
SELECT @numa = COUNT(DISTINCT parent_node_id) FROM sys.dm_os_schedulers WHERE scheduler_id < 255 AND parent_node_id < 64;
SELECT @clustered = CONVERT(bit,ISNULL(SERVERPROPERTY('IsClustered'),0));

DECLARE @machineinfo TABLE ([Value] NVARCHAR(256), [Data] NVARCHAR(256))

IF @ostype = 'Windows'
BEGIN
	INSERT INTO @machineinfo
	EXEC xp_instance_regread 'HKEY_LOCAL_MACHINE','HARDWARE\DESCRIPTION\System\BIOS','SystemManufacturer';
END;

SELECT @SystemManufacturer = [Data] FROM @machineinfo WHERE [Value] = 'SystemManufacturer';
--

SELECT 
  @maxservermem as config_max_memory,
	CASE WHEN @arch IS NULL THEN '[WARNING: Could not determine architecture needed for check]'
		WHEN (@systemmem <= 2048 AND @maxservermem > @systemmem-512-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END))- CASE WHEN @arch = 32 THEN 256 ELSE 0 END) OR
		(@systemmem BETWEEN 2049 AND 4096 AND @maxservermem > @systemmem-819-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END))- CASE WHEN @arch = 32 THEN 256 ELSE 0 END) OR
		(@systemmem BETWEEN 4097 AND 8192 AND @maxservermem > @systemmem-1228-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END))- CASE WHEN @arch = 32 THEN 256 ELSE 0 END) OR
		(@systemmem BETWEEN 8193 AND 12288 AND @maxservermem > @systemmem-2048-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END))- CASE WHEN @arch = 32 THEN 256 ELSE 0 END) OR
		(@systemmem BETWEEN 12289 AND 24576 AND @maxservermem > @systemmem-2560-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END))- CASE WHEN @arch = 32 THEN 256 ELSE 0 END) OR
		(@systemmem BETWEEN 24577 AND 32768 AND @maxservermem > @systemmem-3072-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END))- CASE WHEN @arch = 32 THEN 256 ELSE 0 END) OR
		(@systemmem > 32768 AND SERVERPROPERTY('EditionID') IN (284895786, 1293598313) AND @maxservermem > CAST(0.5 * (((@systemmem-4096-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)- CASE WHEN @arch = 32 THEN 256 ELSE 0 END)) + 65536) - ABS((@systemmem-4096-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)- CASE WHEN @arch = 32 THEN 256 ELSE 0 END)) - 65536)) AS int)) OR -- Find min of max mem for machine or max mem for Web and Business Intelligence SKU
		(@systemmem > 32768 AND SERVERPROPERTY('EditionID') = -1534726760 AND @maxservermem > CAST(0.5 * (((@systemmem-4096-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)- CASE WHEN @arch = 32 THEN 256 ELSE 0 END)) + 131072) - ABS((@systemmem-4096-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)- CASE WHEN @arch = 32 THEN 256 ELSE 0 END)) - 131072)) AS int)) OR -- Find min of max mem for machine or max mem for Standard SKU
		(@systemmem > 32768 AND SERVERPROPERTY('EngineEdition') IN (3,8) AND @maxservermem > @systemmem-4096-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)- CASE WHEN @arch = 32 THEN 256 ELSE 0 END)) THEN 'WARNING: Not at the recommended MaxMem setting for this server memory configuration, with a single instance' -- Enterprise Edition or Managed Instance
		ELSE 'OK'
	END AS [Deviation],		
	CASE WHEN @systemmem <= 2048 THEN @systemmem-512-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)- CASE WHEN @arch = 32 THEN 256 ELSE 0 END)
		WHEN @systemmem BETWEEN 2049 AND 4096 THEN @systemmem-819-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)- CASE WHEN @arch = 32 THEN 256 ELSE 0 END)
		WHEN @systemmem BETWEEN 4097 AND 8192 THEN @systemmem-1228-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)- CASE WHEN @arch = 32 THEN 256 ELSE 0 END)
		WHEN @systemmem BETWEEN 8193 AND 12288 THEN @systemmem-2048-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)- CASE WHEN @arch = 32 THEN 256 ELSE 0 END)
		WHEN @systemmem BETWEEN 12289 AND 24576 THEN @systemmem-2560-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)- CASE WHEN @arch = 32 THEN 256 ELSE 0 END)
		WHEN @systemmem BETWEEN 24577 AND 32768 THEN @systemmem-3072-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)- CASE WHEN @arch = 32 THEN 256 ELSE 0 END)
		WHEN @systemmem > 32768 AND SERVERPROPERTY('EditionID') IN (284895786, 1293598313) THEN CAST(0.5 * (((@systemmem-4096-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)- CASE WHEN @arch = 32 THEN 256 ELSE 0 END)) + 65536) - ABS((@systemmem-4096-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)- CASE WHEN @arch = 32 THEN 256 ELSE 0 END)) - 65536)) AS int) -- Find min of max mem for machine or max mem for Web and Business Intelligence SKU
		WHEN @systemmem > 32768 AND SERVERPROPERTY('EditionID') = -1534726760 THEN CAST(0.5 * (((@systemmem-4096-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)- CASE WHEN @arch = 32 THEN 256 ELSE 0 END)) + 131072) - ABS((@systemmem-4096-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)- CASE WHEN @arch = 32 THEN 256 ELSE 0 END)) - 131072)) AS int) -- Find min of max mem for machine or max mem for Standard SKU
		WHEN @systemmem > 32768 AND SERVERPROPERTY('EngineEdition') IN (3,8) THEN @systemmem-4096-(@mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)- CASE WHEN @arch = 32 THEN 256 ELSE 0 END) -- Enterprise Edition or Managed Instance
	END AS [Recommended_MaxMem_MB_SingleInstance],
	CASE WHEN @systemmem <= 2048 THEN 512
		WHEN @systemmem BETWEEN 2049 AND 4096 THEN 819
		WHEN @systemmem BETWEEN 4097 AND 8192 THEN 1228
		WHEN @systemmem BETWEEN 8193 AND 12288 THEN 2048
		WHEN @systemmem BETWEEN 12289 AND 24576 THEN 2560
		WHEN @systemmem BETWEEN 24577 AND 32768 THEN 3072
		WHEN @systemmem > 32768 THEN 4096
	END AS [Mem_MB_for_OS],
	CASE WHEN @systemmem <= 2048 THEN @mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)
		WHEN @systemmem BETWEEN 2049 AND 4096 THEN @mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)
		WHEN @systemmem BETWEEN 4097 AND 8192 THEN @mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)
		WHEN @systemmem BETWEEN 8193 AND 12288 THEN @mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)
		WHEN @systemmem BETWEEN 12289 AND 24576 THEN @mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)
		WHEN @systemmem BETWEEN 24577 AND 32768 THEN @mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)
		WHEN @systemmem > 32768 THEN @mwthreads_count*(CASE WHEN @arch = 64 THEN 2 WHEN @arch = 128 THEN 4 WHEN @arch = 32 THEN 0.5 END)
	END AS [Potential_threads_mem_MB],
	@mwthreads_count AS [Configured_workers];

