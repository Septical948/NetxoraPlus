using System.IO;
using System.Net;
using System.Text;
using DBACheck2.App.Models;

namespace DBACheck2.App.Services;

public sealed class AssessmentExportService
{
    public async Task ExportHtmlAsync(AssessmentRun run,string path)
    {
        if(run.Mode!=AssessmentMode.Full)
            throw new InvalidOperationException("Only Full assessments can be exported.");

        var html=BuildHtml(run);
        await File.WriteAllTextAsync(path,html,new UTF8Encoding(true));
    }

    public string SuggestedFileName(AssessmentRun run)
    {
        static string Clean(string value)
        {
            var invalid=Path.GetInvalidFileNameChars();
            return string.Concat((value??"").Select(ch=>invalid.Contains(ch)?'_':ch));
        }

        var host=Clean(run.Host.Replace("\\","_").Replace(":","_"));
        var profile=Clean(run.ProfileName);
        return $"DBACHECK2_Assessment_{run.Engine}_{profile}_{host}_{run.StartedAt:yyyyMMdd_HHmm}.html";
    }

    private static string BuildHtml(AssessmentRun run)
    {
        static string H(string? value)=>WebUtility.HtmlEncode(value??"");
        static string StatusClass(string status)=>status switch
        {
            "CRITICAL" or "ERROR"=>"critical",
            "WARNING"=>"warning",
            "OK"=>"ok",
            "NO PERMISSION" or "UNSUPPORTED" or "UNAVAILABLE" or "NOT ENABLED"=>"info",
            _=>"info"
        };

        var ordered=run.Checks
            .OrderByDescending(x=>x.Severity)
            .ThenBy(x=>x.Category)
            .ThenBy(x=>x.Title)
            .ToList();

        var rows=new StringBuilder();
        foreach(var x in ordered)
        {
            rows.Append($@"<tr>
<td><span class='pill {StatusClass(x.Status)}'>{H(x.Status)}</span></td>
<td>{H(x.Category.ToString())}</td>
<td>{H(x.Title)}</td>
<td>{H(x.Summary)}</td>
<td>{H(x.Capability)}</td>
<td class='num'>{x.DurationMs}</td>
</tr>");
        }

        var detail=new StringBuilder();
        foreach(var x in ordered)
        {
            detail.Append($@"<section class='check'>
<div class='check-head'>
  <div>
    <span class='pill {StatusClass(x.Status)}'>{H(x.Status)}</span>
    <strong>{H(x.Category.ToString())} · {H(x.Title)}</strong>
  </div>
  <div class='muted'>{H(x.Capability)}</div>
</div>
<div class='summary'>{H(x.Summary)}</div>
<div class='detail-grid'>
  <div><h4>Evidence</h4><pre>{H(x.Evidence)}</pre></div>
  <div><h4>Why it matters</h4><p>{H(x.WhyItMatters)}</p></div>
  <div><h4>Recommended action</h4><p>{H(x.RecommendedAction)}</p></div>
  <div><h4>Verification</h4><p>{H(x.Verification)}</p></div>
</div>
<div class='safety'>Safety: {(x.ReadOnly?"READ-ONLY":"IMPACT")} · Duration: {x.DurationMs} ms</div>
</section>");
        }

        return $@"<!doctype html>
<html lang='en'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<title>DBACHECK2 Assessment - {H(run.ProfileName)}</title>
<style>
:root{{--bg:#0a0d18;--panel:#111628;--panel2:#111c2f;--line:#2e3a55;--text:#e8edf7;--muted:#8f9bb3;--cyan:#38e8d0;--warn:#f0b45a;--crit:#ff6b7a;--ok:#36d399;--purple:#bda7ff}}
*{{box-sizing:border-box}} body{{margin:0;background:var(--bg);color:var(--text);font:14px/1.45 Segoe UI,Arial,sans-serif}}
.container{{max-width:1500px;margin:0 auto;padding:28px}}
header{{border-bottom:1px solid var(--line);padding-bottom:18px;margin-bottom:20px}}
.brand{{color:var(--cyan);font-size:26px;font-weight:800;letter-spacing:.5px}}
.sub{{color:var(--muted)}} .meta{{display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr));gap:10px;margin-top:16px}}
.card{{background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:12px 14px}}
.label{{color:var(--muted);font-size:11px;text-transform:uppercase;font-weight:700}} .value{{font-size:16px;margin-top:4px}}
.kpis{{display:grid;grid-template-columns:repeat(5,1fr);gap:10px;margin:18px 0}}
.kpi{{background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:12px}} .kpi b{{display:block;font-size:22px}}
table{{width:100%;border-collapse:collapse;background:var(--panel2);border:1px solid var(--line)}} th,td{{padding:9px 10px;border-bottom:1px solid #22304a;text-align:left;vertical-align:top}} th{{background:#20314d;position:sticky;top:0}}
.num{{text-align:right}} .pill{{display:inline-block;min-width:82px;text-align:center;border-radius:999px;padding:3px 8px;font-size:11px;font-weight:800;margin-right:8px}}
.critical{{background:#4a2028;color:#ffd7dd}} .warning{{background:#47381d;color:#ffd98a}} .ok{{background:#173d35;color:#9df7dc}} .info{{background:#2a2448;color:#d7caff}}
h2{{color:var(--cyan);margin-top:28px}} .check{{background:var(--panel2);border:1px solid var(--line);border-radius:10px;padding:16px;margin:12px 0}}
.check-head{{display:flex;justify-content:space-between;gap:14px}} .muted{{color:var(--muted)}} .summary{{font-weight:600;margin:12px 0}}
.detail-grid{{display:grid;grid-template-columns:1fr 1fr;gap:14px}} .detail-grid>div{{background:#0d1424;border:1px solid #22304a;border-radius:8px;padding:12px}}
h4{{margin:0 0 8px;color:#9fb3d1;text-transform:uppercase;font-size:11px}} pre{{white-space:pre-wrap;word-break:break-word;margin:0;font:12px/1.45 Consolas,monospace}}
.safety{{margin-top:10px;color:var(--muted);font-size:12px}} footer{{margin-top:28px;color:var(--muted);font-size:12px}}
@media print{{body{{background:white;color:black}} .card,.kpi,.check,table{{break-inside:avoid;background:white;color:black}} th{{background:#eee}}}}
</style>
</head>
<body>
<div class='container'>
<header>
  <div class='brand'>NETXORA · DBACHECK2</div>
  <div class='sub'>Full Assessment Report · Read-only evidence</div>
  <div class='meta'>
    <div class='card'><div class='label'>Assessment ID</div><div class='value'>{H(run.RunId)}</div></div>
    <div class='card'><div class='label'>Profile</div><div class='value'>{H(run.ProfileName)}</div></div>
    <div class='card'><div class='label'>Host</div><div class='value'>{H(run.Host)}</div></div>
    <div class='card'><div class='label'>Engine</div><div class='value'>{H(run.Engine.ToString())}</div></div>
    <div class='card'><div class='label'>Pack</div><div class='value'>{H(run.PackVersion)}</div></div>
    <div class='card'><div class='label'>Started</div><div class='value'>{run.StartedAt:yyyy-MM-dd HH:mm:ss}</div></div>
    <div class='card'><div class='label'>Completed</div><div class='value'>{run.CompletedAt:yyyy-MM-dd HH:mm:ss}</div></div>
    <div class='card'><div class='label'>Duration</div><div class='value'>{run.DurationSeconds:0.0}s</div></div>
  </div>
</header>

<div class='kpis'>
  <div class='kpi'><div class='label'>Overall</div><b>{H(run.Overall)}</b></div>
  <div class='kpi'><div class='label'>Critical</div><b>{run.Critical}</b></div>
  <div class='kpi'><div class='label'>Warning</div><b>{run.Warning}</b></div>
  <div class='kpi'><div class='label'>OK</div><b>{run.Ok}</b></div>
  <div class='kpi'><div class='label'>Info / Capability</div><b>{run.Info}</b></div>
</div>

<h2>Assessment Summary</h2>
<table>
<thead><tr><th>Status</th><th>Category</th><th>Check</th><th>Summary</th><th>Capability</th><th>ms</th></tr></thead>
<tbody>{rows}</tbody>
</table>

<h2>Detailed Evidence</h2>
{detail}

<footer>
Provider: {H(run.ProviderInfo)}<br>
Generated by DBACHECK2 from the stored Full Assessment. This report does not execute remediation actions.
</footer>
</div>
</body>
</html>";
    }
}
