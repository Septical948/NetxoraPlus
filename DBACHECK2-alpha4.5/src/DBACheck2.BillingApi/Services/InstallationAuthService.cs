using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace DBACheck2.BillingApi.Services;

public sealed class InstallationAuthService
{
    private readonly BillingStore _store;
    public InstallationAuthService(BillingStore store)=>_store=store;

    public async Task<bool> AuthenticateOrRegisterAsync(string installationId,string secret)
    {
        if(string.IsNullOrWhiteSpace(installationId) || string.IsNullOrWhiteSpace(secret))return false;
        if(secret.Length<32)return false;

        var hash=Hash(secret);
        await using var cn=new SqliteConnection($"Data Source={_store.DatabasePath}");
        await cn.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();

        await using var read=cn.CreateCommand();
        read.Transaction=(SqliteTransaction)tx;
        read.CommandText="SELECT secret_hash FROM installation_credentials WHERE installation_id=$id;";
        read.Parameters.AddWithValue("$id",installationId);
        var stored=Convert.ToString(await read.ExecuteScalarAsync());

        if(string.IsNullOrWhiteSpace(stored))
        {
            await using var insert=cn.CreateCommand();
            insert.Transaction=(SqliteTransaction)tx;
            insert.CommandText=@"INSERT INTO installation_credentials(installation_id,secret_hash,created_at,last_seen_at)
VALUES($id,$hash,$now,$now);";
            insert.Parameters.AddWithValue("$id",installationId);
            insert.Parameters.AddWithValue("$hash",hash);
            insert.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));
            await insert.ExecuteNonQueryAsync();
            await tx.CommitAsync();
            return true;
        }

        var ok=FixedEquals(stored,hash);
        if(ok)
        {
            await using var update=cn.CreateCommand();
            update.Transaction=(SqliteTransaction)tx;
            update.CommandText="UPDATE installation_credentials SET last_seen_at=$now WHERE installation_id=$id;";
            update.Parameters.AddWithValue("$id",installationId);
            update.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));
            await update.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
        else await tx.RollbackAsync();

        return ok;
    }

    public static string ReadSecret(HttpRequest request)
        =>request.Headers["X-DBACHECK-Installation-Secret"].ToString();

    private static string Hash(string value)
        =>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedEquals(string a,string b)
    {
        try
        {
            var aa=Convert.FromHexString(a);
            var bb=Convert.FromHexString(b);
            return aa.Length==bb.Length && CryptographicOperations.FixedTimeEquals(aa,bb);
        }
        catch{return false;}
    }
}
