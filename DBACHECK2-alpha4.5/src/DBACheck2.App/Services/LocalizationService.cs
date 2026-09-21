using System.IO;
using System.Text.Json;

namespace DBACheck2.App.Services;

public enum AppLanguage { Es, En }

public static class LocalizationService
{
    private sealed class Pref { public string Language { get; set; } = "es"; }
    private static readonly string FilePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Netxora","DBACHECK2","preferences.json");
    public static AppLanguage Current { get; private set; }=Load();

    private static readonly Dictionary<string,(string Es,string En)> S=new(StringComparer.OrdinalIgnoreCase)
    {
        ["App.Subtitle"]=("Beta 1 · Plataforma DBA Multi-Engine","Beta 1 · Multi-Engine DBA Platform"),
        ["Connection.Engine"]=("MOTOR","ENGINE"), ["Connection.Target"]=("DESTINO / PERFIL","TARGET / PROFILE"), ["Connection.Test"]=("PROBAR CONEXIÓN","TEST CONNECTION"),
        ["Connection.Unverified"]=("NO VERIFICADA","NOT VERIFIED"), ["Connection.Trust"]=("Confiar certificado","Trust certificate"),
        ["Nav.Operation"]=("OPERACIÓN","OPERATIONS"), ["Nav.Quick"]=("Chequeo rápido","Quick Check"),
        ["Nav.LongTransactions"]=("Transacciones largas","Long Transactions"), ["Nav.Blocking"]=("Bloqueos","Blocking"),
        ["Nav.TempSql"]=("TempDB / Version Store","TempDB / Version Store"), ["Nav.TempPg"]=("Vacuum / Uso temporal","Vacuum / Temp Usage"),
        ["Nav.TempOracle"]=("TEMP / UNDO","TEMP / UNDO"), ["Nav.TempMySql"]=("Temporales / InnoDB","Temp / InnoDB"),
        ["Nav.LogSql"]=("Transaction Log","Transaction Log"), ["Nav.LogPg"]=("WAL / XLOG","WAL / XLOG"), ["Nav.LogOracle"]=("Redo / Archive","Redo / Archive"), ["Nav.LogMySql"]=("Redo / Binlog","Redo / Binlog"),
        ["Nav.BackupSql"]=("Backups / Jobs","Backups / Jobs"), ["Nav.BackupPg"]=("Backup / Mantenimiento","Backup / Maintenance"), ["Nav.BackupOracle"]=("Backup / Scheduler","Backup / Scheduler"), ["Nav.BackupMySql"]=("Backup / Events","Backup / Events"),
        ["Nav.HaSql"]=("AlwaysOn / HA","AlwaysOn / HA"), ["Nav.HaPg"]=("Replicación Streaming","Streaming Replication"), ["Nav.HaOracle"]=("Data Guard / HA","Data Guard / HA"), ["Nav.HaMySql"]=("Replicación","Replication"),
        ["Nav.PerfSql"]=("Rendimiento","Performance"), ["Nav.PerfPg"]=("Rendimiento / Índices","Performance / Indexes"), ["Nav.PerfOracle"]=("Rendimiento / SQL","Performance / SQL"), ["Nav.PerfMySql"]=("Rendimiento / Índices","Performance / Indexes"),
        ["Nav.History"]=("Historial de incidentes","Incident History"), ["Nav.Operations"]=("Operaciones de incidentes","Incident Operations"), ["Nav.Assistant"]=("Conexiones / Perfiles","Connections / Profiles"),
        ["Context.Current"]=("CONTEXTO ACTUAL","CURRENT CONTEXT"), ["Context.WindowsAuth"]=("Autenticación Windows","Windows Authentication"), ["Context.ProfileRequired"]=("Seleccioná o creá un perfil para credenciales","Select or create a profile for credentials"),
        ["Quick.Title"]=("CHEQUEO RÁPIDO","QUICK CHECK"), ["Quick.Subtitle"]=("Estado operativo del destino seleccionado. Solo lectura.","Operational status of the selected target. Read-only."),
        ["Quick.Ready"]=("Listo. Seleccioná el destino / perfil y ejecutá el chequeo rápido.","Ready. Select the target / profile and run Quick Check."), ["Quick.SelectEvidence"]=("Seleccioná una fila para ver la evidencia completa.","Select a row to view the complete evidence."),
        ["Grid.Status"]=("Estado","Status"), ["Grid.Area"]=("Área","Area"), ["Grid.Summary"]=("Resumen","Summary"), ["Grid.Evidence"]=("Evidencia / detalle","Evidence / detail"),
        ["Grid.EvidenceTitle"]=("EVIDENCIA DEL CHECK SELECCIONADO","SELECTED CHECK EVIDENCE"), ["Insight.Health"]=("ESTADO","HEALTH"), ["Insight.Alerts"]=("ALERTAS","ALERTS"), ["Insight.Top"]=("HALLAZGO PRINCIPAL","TOP FINDING"), ["Insight.Engine"]=("MOTOR / CONTEXTO","ENGINE / CONTEXT"),
        ["Common.ReadOnly"]=("Solo lectura","Read-only"), ["Language.Label"]=("IDIOMA","LANGUAGE"),
        ["Profiles.Title"]=("PERFILES DE SERVIDOR","SERVER PROFILES"), ["Profiles.ConnectionTitle"]=("CONEXIÓN / PERFILES","CONNECTION / PROFILES"), ["Profiles.Connect"]=("CONECTAR","CONNECT"), ["Profiles.Save"]=("GUARDAR","SAVE"), ["Profiles.Load"]=("CARGAR","LOAD"), ["Profiles.Use"]=("USAR PERFIL","USE PROFILE"),
        ["Profiles.Test"]=("PROBAR","TEST"), ["Profiles.Ask"]=("PREGUNTAR","ASK"), ["Profiles.Question"]=("¿Qué problemas tiene este servidor?","What problems does this server have?"),
        ["Profiles.Select"]=("Seleccioná o guardá un perfil.","Select or save a profile."), ["Profiles.Required"]=("Nombre y Host son obligatorios.","Name and Host are required."),
        ["Profiles.HostRequired"]=("Host obligatorio.","Host is required."), ["Profiles.Testing"]=("Probando conexión...","Testing connection..."), ["Profiles.Collecting"]=("Recolectando evidencia...","Collecting evidence..."),
        ["Profiles.GlobalActive"]=("PERFIL GLOBAL ACTIVO","GLOBAL PROFILE ACTIVE"), ["Profiles.ContextUpdated"]=("El contexto superior de DBACHECK fue actualizado.","DBACHECK global context was updated."),
        ["Profiles.PasswordRuntime"]=("Password (solo sesión)","Password (session only)"), ["Profiles.ServiceSid"]=("Service Name / SID","Service Name / SID"),
        ["Status.Unsupported"]=("No soportado por esta versión","Unsupported by this version"), ["Status.NotEnabled"]=("Disponible pero no habilitado","Available but not enabled"),
        ["Status.NoPermission"]=("Permisos insuficientes","Insufficient privileges"), ["Status.Unavailable"]=("No disponible en esta muestra","Unavailable in this sample")
    };

    public static string T(string key)=>S.TryGetValue(key,out var v)?(Current==AppLanguage.Es?v.Es:v.En):key;
    public static void Set(AppLanguage language){Current=language;var dir=Path.GetDirectoryName(FilePath)!;Directory.CreateDirectory(dir);File.WriteAllText(FilePath,JsonSerializer.Serialize(new Pref{Language=language==AppLanguage.Es?"es":"en"}));}
    private static AppLanguage Load(){try{if(File.Exists(FilePath)){var p=JsonSerializer.Deserialize<Pref>(File.ReadAllText(FilePath));if(string.Equals(p?.Language,"en",StringComparison.OrdinalIgnoreCase))return AppLanguage.En;}}catch{}return AppLanguage.Es;}
}
