/*
 Guid Clustered index
*/

use master;
set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

-- Get Lines Stores SP
with c 
as
(
SELECT convert(varchar(512),'TriggerdbHeader') AS Database_Name,
	t.name AS schemaName, 
	mi.[name] AS Index_Name, 
	SUBSTRING((SELECT ',' + ac.name FROM sys.tables AS st
		INNER JOIN sys.indexes AS i ON st.[object_id] = i.[object_id]
		INNER JOIN sys.index_columns AS ic ON i.[object_id] = ic.[object_id] AND i.[index_id] = ic.[index_id] 
		INNER JOIN sys.all_columns AS ac ON st.[object_id] = ac.[object_id] AND ic.[column_id] = ac.[column_id]
		WHERE mi.[object_id] = i.[object_id] AND mi.index_id = i.index_id AND ic.is_included_column = 0
		ORDER BY ic.key_ordinal
	FOR XML PATH('')), 2, 8000) AS KeyCols,
	(SELECT COUNT(sty.name) FROM sys.indexes AS i
		INNER JOIN sys.tables AS t ON t.[object_id] = i.[object_id]
		INNER JOIN sys.schemas ss ON ss.[schema_id] = t.[schema_id]
		INNER JOIN sys.index_columns AS sic ON sic.object_id = mst.object_id AND sic.index_id = mi.index_id
		INNER JOIN sys.columns AS sc ON sc.object_id = t.object_id AND sc.column_id = sic.column_id
		INNER JOIN sys.types AS sty ON sc.user_type_id = sty.user_type_id
		) AS [Key_has_GUID]
FROM sys.indexes AS mi
INNER JOIN sys.tables AS mst ON mst.[object_id] = mi.[object_id]
INNER JOIN sys.schemas AS t ON t.[schema_id] = mst.[schema_id]
	--AND OBJECTPROPERTY(o.object_id,''IsUserTable'') = 1 -- sys.tables only returns type U
)
select  top (1) * into #result from c
OPTION (MAXDOP 2)



DECLARE @SQL VARCHAR(4000)

DECLARE @DBNAME SYSNAME
DECLARE BASES CURSOR FOR
SELECT NAME FROM SYS.DATABASES WHERE 
[state_desc] = 'ONLINE' 
AND [source_database_id] IS NULL 
AND [database_id] > 4   AND 
DATABASEPROPERTYEX(name, 'UserAccess') <> 'SINGLE_USER' 

OPEN BASES;

FETCH NEXT FROM BASES
INTO @DBNAME

WHILE @@FETCH_STATUS = 0
BEGIN

SET @SQL = 
 'USE ' + QUOTENAME(@DBNAME)  +
 ';
 with c 
as
(
SELECT db_name() AS Database_Name,
	t.name AS schemaName, 
	mi.[name] AS Index_Name, 
	SUBSTRING((SELECT '','' + ac.name FROM sys.tables AS st
		INNER JOIN sys.indexes AS i ON st.[object_id] = i.[object_id]
		INNER JOIN sys.index_columns AS ic ON i.[object_id] = ic.[object_id] AND i.[index_id] = ic.[index_id] 
		INNER JOIN sys.all_columns AS ac ON st.[object_id] = ac.[object_id] AND ic.[column_id] = ac.[column_id]
		WHERE mi.[object_id] = i.[object_id] AND mi.index_id = i.index_id AND ic.is_included_column = 0
		ORDER BY ic.key_ordinal
	FOR XML PATH('''')), 2, 8000) AS KeyCols,
	(SELECT COUNT(sty.name) FROM sys.indexes AS i
		INNER JOIN sys.tables AS t ON t.[object_id] = i.[object_id]
		INNER JOIN sys.schemas ss ON ss.[schema_id] = t.[schema_id]
		INNER JOIN sys.index_columns AS sic ON sic.object_id = mst.object_id AND sic.index_id = mi.index_id
		INNER JOIN sys.columns AS sc ON sc.object_id = t.object_id AND sc.column_id = sic.column_id
		INNER JOIN sys.types AS sty ON sc.user_type_id = sty.user_type_id
		WHERE mi.[object_id] = i.[object_id] AND mi.index_id = i.index_id AND sic.is_included_column = 0 
		AND sty.name = ''uniqueidentifier'') AS [Key_has_GUID]
FROM sys.indexes AS mi
INNER JOIN sys.tables AS mst ON mst.[object_id] = mi.[object_id]
INNER JOIN sys.schemas AS t ON t.[schema_id] = mst.[schema_id]
WHERE mi.type = 1 AND mi.is_unique_constraint = 0
	AND mst.is_ms_shipped = 0
	--AND OBJECTPROPERTY(o.object_id,''''IsUserTable'''') = 1 -- sys.tables only returns type U
)
select * from c
where c.Key_has_GUID =1
OPTION (MAXDOP 2)

 '
 
 INSERT INTO #Result 
 EXEC (@SQL)

FETCH NEXT FROM BASES
INTO @DBNAME

END

CLOSE BASES
DEALLOCATE BASES

SELECT * FROM #Result 
DROP TABLE #Result 


