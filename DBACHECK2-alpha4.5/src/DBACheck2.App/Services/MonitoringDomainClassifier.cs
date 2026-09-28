using DBACheck2.App.Models;

namespace DBACheck2.App.Services;

public static class MonitoringDomainClassifier
{
    public static string Classify(IntegrationEvent e)
    {
        var text=$"{e.Host} {e.Name} {e.Tags} {e.RawDetail}".ToLowerInvariant();

        if(Has(text,
            "sql server","mssql","database","db "," db","oracle","postgres","postgresql","mysql","mariadb",
            "mongodb","firebird","listener","tablespace","tempdb","transaction log","wal","xlog","redo",
            "binlog","replication","replica","alwayson","availability group","dataguard","rman",
            "deadlock","blocking","blocked session","lock wait","query","slow query","db connection"))
            return "DATABASE";

        if(Has(text,
            "windows","linux","unix","ubuntu","red hat","rhel","centos","systemd","kernel","reboot",
            "uptime","service has been restarted","service stopped","service down","process","cpu","memory",
            "ram","swap","filesystem","file system","disk space","free space","load average","agent",
            "googleupdater","os "))
            return "OS";

        if(Has(text,
            "switch","router","firewall","network","interface","ethernet","gigabit","link down","link up",
            "packet loss","latency","icmp","snmp","port down","port up","bandwidth","wan","lan","vlan"))
            return "NETWORK";

        if(Has(text,
            "iis","apache","nginx","tomcat","websphere","application","app ","website","http","https",
            "api ","api_","endpoint","jvm",".net","java","queue","rabbitmq","kafka"))
            return "APPLICATION";

        return "OTHER";
    }

    public static string Display(string domain,bool english)=>domain switch
    {
        "DATABASE"=>english?"Database":"Base de datos",
        "OS"=>english?"OS":"SO",
        "NETWORK"=>english?"Network":"Red",
        "APPLICATION"=>english?"Application":"Aplicación",
        "OTHER"=>english?"Other":"Otros",
        _=>english?"All":"Todos"
    };

    private static bool Has(string text,params string[] values)=>values.Any(text.Contains);
}
