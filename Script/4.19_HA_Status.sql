set nocount on
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED

declare @versionsql INT

select @versionsql = 
CONVERT(INT,left(convert(CHAR(2),SERVERPROPERTY('ProductVersion')),2))

if @versionsql > 12  
begin
SELECT 'HA_ENABLE' AS PROPIEDAD, case when   
SERVERPROPERTY('IsClustered') =1 OR 
SERVERPROPERTY('IsHadrEnabled') =1 then 
'Yes' ELSE 'NO' end as Status
end 

else
 begin
SELECT 'HA_ENABLE' AS PROPIEDAD, case when   
SERVERPROPERTY('IsClustered') =1 then 
'Yes' ELSE 'NO' end as Status
end 




