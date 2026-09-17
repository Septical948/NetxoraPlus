set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED


IF OBJECT_ID('tempdb..#IDX_Table') IS NOT NULL  
    DROP TABLE #IDX_Table; 
 
IF OBJECT_ID('tempdb..#FK_Table') IS NOT NULL     
    DROP TABLE #FK_Table; 
 
--Create index temp table 
CREATE TABLE #IDX_Table 
( 
    base sysname,
    IDX_Table sysname, 
    IDX_Name sysname,  
    IDX_Columns varchar(4000), 
    IDX_IncludedColumns varchar(4000) 
); 
 
--Create FK temp table  
CREATE TABLE #FK_Table 
( 
    base sysname,
    FK_Name sysname,  
    FK_Table sysname, 
    FK_Columns varchar(4000), 
    PK_Table sysname, 
    PK_Columns varchar(4000) 
); 


DECLARE @SQL VARCHAR(4000)

DECLARE @DBNAME SYSNAME
DECLARE BASES CURSOR FOR
SELECT NAME FROM SYS.DATABASES WHERE 
[state_desc] = 'ONLINE' 
AND [source_database_id] IS NULL 
AND [database_id] > 4 

OPEN BASES;

FETCH NEXT FROM BASES
INTO @DBNAME

WHILE @@FETCH_STATUS = 0
BEGIN

SET @SQL = 
 'USE ' + QUOTENAME(@DBNAME)  + '; WITH CTE AS 
(     
SELECT  
    ic.index_id + ic.object_id AS IndexId, 
    s.name+''.''+t.name AS TableName, 
    I.name AS IndexName, 
    case  
        when ic.is_included_column =0  
        then c.name  
    end as ColumnName, 
    case  
        when ic.is_included_column =1  
        then c.name  
    end as IncludedColumn, 
    i.type_desc, 
    i.is_primary_key, 
    i.is_unique  
FROM  sys.indexes i  
INNER JOIN sys.index_columns ic  
    ON  i.index_id    =   ic.index_id  
    AND i.object_id   =   ic.object_id  
INNER JOIN sys.columns c  
    ON  ic.column_id  =   c.column_id  
    AND i.object_id   =   c.object_id  
INNER JOIN sys.tables t  
    ON  i.object_id = t.object_id  
INNER JOIN sys.schemas S 
    ON t.schema_id=s.schema_id 
)  
 
INSERT INTO #IDX_Table 
    SELECT  DB_NAME(),
        c.TableName TABLE_NAME, 
        c.IndexName IDX_NAME, 
        STUFF( ( SELECT '',''+ a.ColumnName FROM CTE a WHERE c.IndexId = a.IndexId FOR XML PATH('''')),1 ,1, '''') AS COLUMNS, 
        STUFF( ( SELECT '',''+ a.IncludedColumn FROM CTE a WHERE c.IndexId = a.IndexId FOR XML PATH('''')),1 ,1, '''') AS INCLUDED_COLUMNS 
    FROM   CTE c  
    GROUP  BY c.IndexId,c.TableName,c.IndexName 
    ORDER  BY c.TableName ASC;  
 
 
WITH CTE_FK AS 
(SELECT fk.name AS FK_Name,  
    CONVERT(VARCHAR(100),Fk.parent_object_id)+CONVERT(VARCHAR(100),fk.object_id)FK_Id, 
    s1.name + ''.'' + t1.name  AS FK_Table, 
    COL_NAME(fkc.parent_object_id,fkc.parent_column_id) AS FK_Column, 
    s2.name + ''.'' + t2.name  AS PK_Table, 
    COL_NAME(fkc.referenced_object_id,fkc.referenced_column_id) AS PK_Column 
FROM sys.foreign_keys fk  
JOIN sys.tables t1  
    ON fk.parent_object_id= t1.object_id 
JOIN sys.tables t2     
    ON fk.referenced_object_id= t2.object_id 
JOIN sys.schemas s1 
    ON t1.schema_id = s1.schema_id 
JOIN sys.schemas s2 
    ON t2.schema_id = s2.schema_id 
Inner join sys.foreign_key_columns fkc 
    ON fk.object_id=fkc.constraint_object_id 
) 
 
INSERT INTO #FK_Table 
SELECT DB_NAME(),
    FK.FK_Name, 
    FK.FK_Table, 
    STUFF( ( SELECT '',''+ c1.FK_Column FROM CTE_FK C1 WHERE FK.FK_Id = C1.fk_id FOR XML PATH('''')),1 ,1, '''') AS FK_Columns, 
    FK.PK_Table,         
    STUFF( ( SELECT '',''+ c1.PK_Column FROM CTE_FK C1 WHERE FK.FK_Id = C1.fk_id FOR XML PATH('''')),1 ,1, '''') AS PK_Columns     
FROM   CTE_FK FK 
GROUP  BY FK.FK_Id,FK.FK_Table,FK.FK_Name,FK.PK_Table;     
 
 '

EXEC (@SQL)

FETCH NEXT FROM BASES
INTO @DBNAME

END

CLOSE BASES
DEALLOCATE BASES


SELECT DISTINCT  
   base, FK_Table, 
    FK_Columns, 
    PK_Table, 
    PK_Columns, 
    FK_Name, 
    IDX_Script= 'CREATE NONCLUSTERED INDEX FK_' +   FK_Name + ' ON ' + FK_Table+ '(' +FK_Columns+ ') GO' 
FROM #FK_Table F1 
WHERE NOT EXISTS  
    (SELECT * 
     FROM #FK_Table F 
    INNER JOIN #idx_table T 
        ON F.FK_Table= t.idx_table 
    WHERE (f1.fk_name = f.fk_name 
        AND FK_Columns= IDX_Columns) 
        OR ( F1.FK_Name = F.fk_name 
        AND FK_Columns= SUBSTRING (IDX_Columns, 1 ,  
            CASE  
            WHEN CHARINDEX( ',',IDX_Columns)= 0 THEN 0  
            ELSE CHARINDEX( ',',IDX_Columns) -1 
            END 
))) 
--FOR XML RAW, ELEMENTS, root ('FKIndex')

