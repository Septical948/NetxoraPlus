using System.Data;
using System.Data.OleDb;
using DBACheck2.App.Models;

namespace DBACheck2.App.Providers;

// Legacy Oracle path for installations such as Oracle 8i/9i.
// It deliberately uses the Oracle OLE DB provider installed on Windows instead of modern ODP.NET.
// DBACHECK2 does not redistribute an obsolete Oracle Client.
public sealed class OracleLegacyProvider : IDatabaseProvider
{
    private readonly ServerProfile p;
    private readonly string cs;
    public OracleLegacyProvider(ServerProfile profile)
    {
        p=profile;
        var port=p.Port??1521;
        var sid=string.IsNullOrWhiteSpace(p.DatabaseOrService)?"ORCL":p.DatabaseOrService;
        var dataSource=$"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={p.Host})(PORT={port}))(CONNECT_DATA=(SID={sid})))";
        cs=$"Provider=OraOLEDB.Oracle;Data Source={dataSource};User ID={p.Username};Password={p.Password};OLEDB.NET=True;";
    }
    public DatabaseEngine Engine=>DatabaseEngine.Oracle;
    public string DisplayName=>"Oracle Legacy (OraOLEDB)";

    public async Task<string> TestAsync()=>await Task.Run(()=>{
        using var c=Open();
        using var q=new OleDbCommand("select banner from v$version where rownum=1",c);
        return Convert.ToString(q.ExecuteScalar())??"Oracle Legacy connected";
    });

    public async Task<List<HealthItem>> QuickCheckAsync()=>await Task.Run(()=>{
        var x=new List<HealthItem>(); using var c=Open();
        var banner=Scalar(c,"select banner from v$version where rownum=1");
        x.Add(new(){Area="ENGINE",Status="INFO",Summary=banner,Detail=$"Legacy OraOLEDB | Host: {p.Host} | SID: {p.DatabaseOrService} | User: {p.Username}"});
        x.Add(Q(c,"SESSIONS","select decode(sign(count(*)-199),1,'WARNING','OK'),to_char(count(*))||' session(s)','ACTIVE='||to_char(sum(decode(status,'ACTIVE',1,0))) from v$session where type='USER'"));
        x.Add(Q(c,"BLOCKING","select decode(sign(count(*)-1),-1,'OK','WARNING'),to_char(count(*))||' blocked session(s)',nvl(max('SID '||to_char(s.sid)||' waits for SID '||to_char(b.sid)),'') from v$lock l1,v$lock l2,v$session s,v$session b where l1.request>0 and l2.lmode>0 and l1.id1=l2.id1 and l1.id2=l2.id2 and s.sid=l1.sid and b.sid=l2.sid"));
        x.Add(Q(c,"TRANSACTIONS","select 'INFO',to_char(count(*))||' open transaction(s)',nvl(max('SID '||to_char(s.sid)||' START='||t.start_time),'') from v$transaction t,v$session s where s.taddr=t.addr"));
        x.Add(Q(c,"TABLESPACE","select decode(sign(max(pct)-89),1,'CRITICAL',decode(sign(max(pct)-79),1,'WARNING','OK')),to_char(round(max(pct),1))||'% max used',max(tablespace_name||'='||to_char(round(pct,1))||'%') from (select df.tablespace_name,100*(1-nvl(fs.free_mb,0)/df.total_mb) pct from (select tablespace_name,sum(bytes)/1024/1024 total_mb from dba_data_files group by tablespace_name) df,(select tablespace_name,sum(bytes)/1024/1024 free_mb from dba_free_space group by tablespace_name) fs where fs.tablespace_name(+)=df.tablespace_name)"));
        x.Add(Q(c,"ARCHIVELOG","select decode(log_mode,'ARCHIVELOG','OK','INFO'),'Log mode: '||log_mode,'Legacy database' from v$database"));
        x.Add(Q(c,"REDO","select 'INFO',to_char(count(*))||' redo log group(s)',to_char(sum(bytes)/1024/1024)||' MB allocated' from v$log"));
        x.Add(Q(c,"INVALID OBJECTS","select decode(count(*),0,'OK','WARNING'),to_char(count(*))||' invalid object(s)',nvl(max(owner||'.'||object_name||' '||object_type),'') from dba_objects where status='INVALID'"));
        return x;
    });

    private OleDbConnection Open()
    {
        try { var c=new OleDbConnection(cs); c.Open(); return c; }
        catch(InvalidOperationException e) when(e.Message.Contains("OraOLEDB",StringComparison.OrdinalIgnoreCase) || e.Message.Contains("provider",StringComparison.OrdinalIgnoreCase))
        { throw new InvalidOperationException("Oracle Legacy requires OraOLEDB.Oracle from an installed Oracle Client. The DBACHECK2 process architecture must match the Oracle Client architecture (x64/x86).",e); }
    }
    private static string Scalar(OleDbConnection c,string sql){using var q=new OleDbCommand(sql,c);return Convert.ToString(q.ExecuteScalar())??"";}
    private static HealthItem Q(OleDbConnection c,string area,string sql)
    {
        try { using var q=new OleDbCommand(sql,c){CommandTimeout=15}; using var r=q.ExecuteReader(); if(r is null||!r.Read())return new(){Area=area,Status="INFO",Summary="No data",Detail=""}; return new(){Area=area,Status=Convert.ToString(r.GetValue(0))??"INFO",Summary=Convert.ToString(r.GetValue(1))??"",Detail=Convert.ToString(r.GetValue(2))??""}; }
        catch(OleDbException e){var m=e.Message.ToUpperInvariant();if(m.Contains("ORA-01031")||m.Contains("ORA-00942"))return new(){Area=area,Status="NO PERMISSION",Summary="Insufficient privileges or dictionary view unavailable",Detail=e.Message};if(m.Contains("ORA-00904")||m.Contains("ORA-00923")||m.Contains("ORA-00933"))return new(){Area=area,Status="UNSUPPORTED",Summary="Collector not supported by this legacy Oracle version",Detail=e.Message};return new(){Area=area,Status="ERROR",Summary="Oracle Legacy collector failed",Detail=e.Message};}
    }
}