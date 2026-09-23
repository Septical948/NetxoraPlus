using System.Diagnostics;
using System.Reflection;
using System.Windows;
using DBACheck2.App.Services;

namespace DBACheck2.App;

public partial class SupportWindow:Window
{
    private const string DocsUrl="https://github.com/Septical948/NetxoraPlus";
    private const string IssuesUrl="https://github.com/Septical948/NetxoraPlus/issues";
    private bool En=>LocalizationService.Current==AppLanguage.En;

    public SupportWindow()
    {
        InitializeComponent();
        ApplyLanguage();
        DiagnosticText.Text=BuildDiagnostics();
    }

    private void ApplyLanguage()
    {
        TitleText.Text=En?"SUPPORT CENTER":"CENTRO DE SOPORTE";
        SubtitleText.Text=En?"Documentation, diagnostics and support resources":"Documentación, diagnóstico y recursos de soporte";
        DocsButton.Content=En?"OPEN DOCUMENTATION":"ABRIR DOCUMENTACIÓN";
        IssuesButton.Content=En?"OPEN SUPPORT / ISSUES":"ABRIR SOPORTE / ISSUES";
        CopyButton.Content=En?"COPY DIAGNOSTICS":"COPIAR DIAGNÓSTICO";
        StatusText.Text=En?"Diagnostic information excludes stored passwords and API tokens.":"La información de diagnóstico excluye passwords guardados y API tokens.";
    }

    private static string BuildDiagnostics()
    {
        var v=Assembly.GetExecutingAssembly().GetName().Version?.ToString()??"unknown";
        return $@"DBACHECK2 SUPPORT DIAGNOSTICS
Version: {v}
OS: {Environment.OSVersion}
64-bit process: {Environment.Is64BitProcess}
Machine: {Environment.MachineName}
User data: {Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\Netxora\DBACHECK2
Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
    }

    private static void Open(string url)=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
    private void DocsButton_Click(object sender,RoutedEventArgs e)=>Open(DocsUrl);
    private void IssuesButton_Click(object sender,RoutedEventArgs e)=>Open(IssuesUrl);
    private void CopyButton_Click(object sender,RoutedEventArgs e){Clipboard.SetText(DiagnosticText.Text);StatusText.Text=En?"Diagnostics copied to clipboard.":"Diagnóstico copiado al portapapeles.";}
}
