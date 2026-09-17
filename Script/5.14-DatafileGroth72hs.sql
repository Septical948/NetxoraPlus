SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED
SET NOCOUNT ON;
SET ANSI_WARNINGS ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @sqlcmd NVARCHAR(max), @params NVARCHAR(600)
DECLARE @sqlmajorver int, @sqlminorver int, @sqlbuild int
DECLARE @ErrorMessage NVARCHAR(4000)
DECLARE @ostype VARCHAR(10)

SELECT @sqlmajorver = CONVERT(int, (@@microsoftversion / 0x1000000) & 0xff);
SELECT @sqlminorver = CONVERT(int, (@@microsoftversion / 0x10000) & 0xff);
SELECT @sqlbuild = CONVERT(int, @@microsoftversion & 0xffff);

IF (@sqlmajorver >= 11) OR (@sqlmajorver = 10 AND @sqlminorver = 50 AND @sqlbuild >= 2500)
BEGIN
	SET @sqlcmd = N'SELECT @ostypeOUT = ''Windows'' FROM sys.dm_os_windows_info (NOLOCK)';
	SET @params = N'@ostypeOUT VARCHAR(10) OUTPUT';
	EXECUTE sp_executesql @sqlcmd, @params, @ostypeOUT=@ostype OUTPUT;
END
ELSE
BEGIN
	SET @ostype = 'Windows'
END;

IF EXISTS (SELECT TOP 1 id FROM sys.traces WHERE is_default = 1)
BEGIN
	DECLARE @tracefilename VARCHAR(500)
	IF @ostype = 'Windows'
	SELECT @tracefilename = LEFT([path],LEN([path]) - PATINDEX('%\%', REVERSE([path]))) + '\log.trc' FROM sys.traces WHERE is_default = 1;
	
	IF @ostype <> 'Windows'
	SELECT @tracefilename = LEFT([path],LEN([path]) - PATINDEX('%/%', REVERSE([path]))) + '/log.trc' FROM sys.traces WHERE is_default = 1;

	WITH AutoGrow_CTE (databaseid, [filename], Growth, Duration, StartTime, EndTime)
	AS
	(
	SELECT databaseid, [filename], SUM(IntegerData*8) AS Growth, Duration, StartTime, EndTime--, CASE WHEN EventClass =
	FROM sys.fn_trace_gettable(@tracefilename, default)
	WHERE EventClass >= 92 AND EventClass <= 95 AND DATEDIFF(hh,StartTime,GETDATE()) < 72 -- Last 24h
	GROUP BY databaseid, [filename], IntegerData, Duration, StartTime, EndTime
	)
	SELECT 'Information' AS [Category], 'Recorded_Autogrows_Lst72H' AS [Information], DB_NAME(database_id) AS Database_Name, 
		mf.name AS logical_file_name, mf.size*8 / 1024 AS size_MB, mf.type_desc,
		ag.Growth AS [growth_KB], CASE WHEN is_percent_growth = 1 THEN 'Pct' ELSE 'MB' END AS growth_type,
		Duration/1000 AS Growth_Duration_ms, ag.StartTime, ag.EndTime
	FROM sys.master_files mf
	LEFT OUTER JOIN AutoGrow_CTE ag ON mf.database_id=ag.databaseid AND mf.name=ag.[filename]
	WHERE ag.Growth > 0 --Only where growth occurred
	GROUP BY database_id, mf.name, mf.size, ag.Growth, ag.Duration, ag.StartTime, ag.EndTime, is_percent_growth, mf.growth, mf.type_desc
	union all
	SELECT top 1 'Information' AS [Category], 'Recorded_Autogrows_Lst72H' AS [Information], 'TriggedbHeader' AS Database_Name, 
		mf.name AS logical_file_name, mf.size*8 / 1024 AS size_MB, mf.type_desc,
		0 AS [growth_KB], CASE WHEN is_percent_growth = 1 THEN 'Pct' ELSE 'MB' END AS growth_type,
		0 AS Growth_Duration_ms, getdate(), getdate()
	FROM sys.master_files mf
	where mf.database_id =1


END
ELSE
BEGIN
	SELECT 'Information' AS [Category], 'Recorded_Autogrows_Lst72H' AS [Information], 'WARNING: Could not gather information on autogrow times' AS [Comment]
END;