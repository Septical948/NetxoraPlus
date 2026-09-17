/*
 Find Index < 900 bytes
*/

use tempdb;
set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT  top 1 convert(varchar(512),'TriggerdbHeader')  AS Database_Name,
	mst.[object_id] AS objectID, t.name AS schemaName, mst.[name] AS objectName, mi.index_id AS indexID, 
	mi.[name] AS Index_Name, mi.[type] AS [indexType], mi.is_primary_key, mi.[is_unique_constraint], mi.is_unique, mi.is_disabled,
	mi.fill_factor, mi.is_padded, 
	SUBSTRING(( SELECT ',' + ac.name FROM sys.tables AS st
		INNER JOIN sys.indexes AS i ON st.[object_id] = i.[object_id]
		INNER JOIN sys.index_columns AS ic ON i.[object_id] = ic.[object_id] AND i.[index_id] = ic.[index_id] 
		INNER JOIN sys.all_columns AS ac ON st.[object_id] = ac.[object_id] AND ic.[column_id] = ac.[column_id]
		WHERE mi.[object_id] = i.[object_id] AND mi.index_id = i.index_id AND ic.is_included_column = 0
		ORDER BY ic.key_ordinal
	FOR XML PATH('')), 2, 8000) AS KeyCols,
	SUBSTRING(( SELECT ',' + ac.name FROM sys.tables AS st
		INNER JOIN sys.indexes AS i ON st.[object_id] = i.[object_id]
		INNER JOIN sys.index_columns AS ic ON i.[object_id] = ic.[object_id] AND i.[index_id] = ic.[index_id] 
		INNER JOIN sys.all_columns AS ac ON st.[object_id] = ac.[object_id] AND ic.[column_id] = ac.[column_id]
		WHERE mi.[object_id] = i.[object_id] AND mi.index_id = i.index_id AND ic.is_included_column = 0
		ORDER BY ac.name
	FOR XML PATH('')), 2, 8000) AS KeyColsOrdered,
	SUBSTRING((SELECT ',' + ac.name FROM sys.tables AS st
		INNER JOIN sys.indexes AS i ON st.[object_id] = i.[object_id]
		INNER JOIN sys.index_columns AS ic ON i.[object_id] = ic.[object_id] AND i.[index_id] = ic.[index_id]
		INNER JOIN sys.all_columns AS ac ON st.[object_id] = ac.[object_id] AND ic.[column_id] = ac.[column_id]
		WHERE mi.[object_id] = i.[object_id] AND mi.index_id = i.index_id AND ic.is_included_column = 1
		ORDER BY ic.key_ordinal
	FOR XML PATH('')), 2, 8000) AS IncludedCols,
	SUBSTRING((SELECT ',' + ac.name FROM sys.tables AS st
		INNER JOIN sys.indexes AS i ON st.[object_id] = i.[object_id]
		INNER JOIN sys.index_columns AS ic ON i.[object_id] = ic.[object_id] AND i.[index_id] = ic.[index_id]
		INNER JOIN sys.all_columns AS ac ON st.[object_id] = ac.[object_id] AND ic.[column_id] = ac.[column_id]
		WHERE mi.[object_id] = i.[object_id] AND mi.index_id = i.index_id AND ic.is_included_column = 1
		ORDER BY ac.name
	FOR XML PATH('')), 2, 8000) AS IncludedColsOrdered,
	SUBSTRING((SELECT ',' + ac.name FROM sys.tables AS st
		INNER JOIN sys.indexes AS i ON st.[object_id] = i.[object_id]
		INNER JOIN sys.index_columns AS ic ON i.[object_id] = ic.[object_id] AND i.[index_id] = ic.[index_id]
		INNER JOIN sys.all_columns AS ac ON st.[object_id] = ac.[object_id] AND ic.[column_id] = ac.[column_id]
		WHERE mi.[object_id] = i.[object_id] AND mi.index_id = i.index_id
		ORDER BY ac.name
	FOR XML PATH('')), 2, 8000) AS AllColsOrdered,
	(SELECT SUM(CASE sty.name WHEN 'nvarchar' THEN sc.max_length/2 ELSE sc.max_length END) FROM sys.indexes AS i
		INNER JOIN sys.tables AS t ON t.[object_id] = i.[object_id]
		INNER JOIN sys.schemas ss ON ss.[schema_id] = t.[schema_id]
		INNER JOIN sys.index_columns AS sic ON sic.object_id = mst.object_id AND sic.index_id = mi.index_id
		INNER JOIN sys.columns AS sc ON sc.object_id = t.object_id AND sc.column_id = sic.column_id
		INNER JOIN sys.types AS sty ON sc.user_type_id = sty.user_type_id
		WHERE mi.[object_id] = i.[object_id] AND mi.index_id = i.index_id AND sic.key_ordinal > 0) AS [KeyCols_data_length_bytes]

into #result
FROM sys.indexes AS mi
INNER JOIN sys.tables AS mst ON mst.[object_id] = mi.[object_id]
INNER JOIN sys.schemas AS t ON t.[schema_id] = mst.[schema_id]
WHERE mi.type IN (1,2,5,6) AND mst.is_ms_shipped = 0
ORDER BY objectName
OPTION (MAXDOP 2);


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

SET @sql = 'SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
USE ' + QUOTENAME(@dbname) + ';
SELECT  db_name()  AS Database_Name,
	mst.[object_id] AS objectID, t.name AS schemaName, mst.[name] AS objectName, mi.index_id AS indexID, 
	mi.[name] AS Index_Name, mi.[type] AS [indexType], mi.is_primary_key, mi.[is_unique_constraint], mi.is_unique, mi.is_disabled,
	mi.fill_factor, mi.is_padded, 
	SUBSTRING(( SELECT '','' + ac.name FROM sys.tables AS st
		INNER JOIN sys.indexes AS i ON st.[object_id] = i.[object_id]
		INNER JOIN sys.index_columns AS ic ON i.[object_id] = ic.[object_id] AND i.[index_id] = ic.[index_id] 
		INNER JOIN sys.all_columns AS ac ON st.[object_id] = ac.[object_id] AND ic.[column_id] = ac.[column_id]
		WHERE mi.[object_id] = i.[object_id] AND mi.index_id = i.index_id AND ic.is_included_column = 0
		ORDER BY ic.key_ordinal
	FOR XML PATH('''')), 2, 8000) AS KeyCols,
	SUBSTRING(( SELECT '','' + ac.name FROM sys.tables AS st
		INNER JOIN sys.indexes AS i ON st.[object_id] = i.[object_id]
		INNER JOIN sys.index_columns AS ic ON i.[object_id] = ic.[object_id] AND i.[index_id] = ic.[index_id] 
		INNER JOIN sys.all_columns AS ac ON st.[object_id] = ac.[object_id] AND ic.[column_id] = ac.[column_id]
		WHERE mi.[object_id] = i.[object_id] AND mi.index_id = i.index_id AND ic.is_included_column = 0
		ORDER BY ac.name
	FOR XML PATH('''')), 2, 8000) AS KeyColsOrdered,
	SUBSTRING((SELECT '','' + ac.name FROM sys.tables AS st
		INNER JOIN sys.indexes AS i ON st.[object_id] = i.[object_id]
		INNER JOIN sys.index_columns AS ic ON i.[object_id] = ic.[object_id] AND i.[index_id] = ic.[index_id]
		INNER JOIN sys.all_columns AS ac ON st.[object_id] = ac.[object_id] AND ic.[column_id] = ac.[column_id]
		WHERE mi.[object_id] = i.[object_id] AND mi.index_id = i.index_id AND ic.is_included_column = 1
		ORDER BY ic.key_ordinal
	FOR XML PATH('''')), 2, 8000) AS IncludedCols,
	SUBSTRING((SELECT '','' + ac.name FROM sys.tables AS st
		INNER JOIN sys.indexes AS i ON st.[object_id] = i.[object_id]
		INNER JOIN sys.index_columns AS ic ON i.[object_id] = ic.[object_id] AND i.[index_id] = ic.[index_id]
		INNER JOIN sys.all_columns AS ac ON st.[object_id] = ac.[object_id] AND ic.[column_id] = ac.[column_id]
		WHERE mi.[object_id] = i.[object_id] AND mi.index_id = i.index_id AND ic.is_included_column = 1
		ORDER BY ac.name
	FOR XML PATH('''')), 2, 8000) AS IncludedColsOrdered,
	SUBSTRING((SELECT '','' + ac.name FROM sys.tables AS st
		INNER JOIN sys.indexes AS i ON st.[object_id] = i.[object_id]
		INNER JOIN sys.index_columns AS ic ON i.[object_id] = ic.[object_id] AND i.[index_id] = ic.[index_id]
		INNER JOIN sys.all_columns AS ac ON st.[object_id] = ac.[object_id] AND ic.[column_id] = ac.[column_id]
		WHERE mi.[object_id] = i.[object_id] AND mi.index_id = i.index_id
		ORDER BY ac.name
	FOR XML PATH('''')), 2, 8000) AS AllColsOrdered,
	(SELECT SUM(CASE sty.name WHEN ''nvarchar'' THEN sc.max_length/2 ELSE sc.max_length END) FROM sys.indexes AS i
		INNER JOIN sys.tables AS t ON t.[object_id] = i.[object_id]
		INNER JOIN sys.schemas ss ON ss.[schema_id] = t.[schema_id]
		INNER JOIN sys.index_columns AS sic ON sic.object_id = mst.object_id AND sic.index_id = mi.index_id
		INNER JOIN sys.columns AS sc ON sc.object_id = t.object_id AND sc.column_id = sic.column_id
		INNER JOIN sys.types AS sty ON sc.user_type_id = sty.user_type_id
		WHERE mi.[object_id] = i.[object_id] AND mi.index_id = i.index_id AND sic.key_ordinal > 0) AS [KeyCols_data_length_bytes]
FROM sys.indexes AS mi
INNER JOIN sys.tables AS mst ON mst.[object_id] = mi.[object_id]
INNER JOIN sys.schemas AS t ON t.[schema_id] = mst.[schema_id]
WHERE mi.type IN (1,2,5,6) AND mst.is_ms_shipped = 0
ORDER BY objectName
OPTION (MAXDOP 2);'
 
 INSERT INTO #Result 
 EXEC (@SQL)

FETCH NEXT FROM BASES
INTO @DBNAME

END

CLOSE BASES
DEALLOCATE BASES

SELECT * FROM #Result
where Database_Name = 'triggerdbheader'
or 
(KeyCols_data_length_bytes > 900 )
 
DROP TABLE #Result 





