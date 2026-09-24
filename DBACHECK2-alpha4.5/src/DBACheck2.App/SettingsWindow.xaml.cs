using System.Windows;
using System.Windows.Controls;
using DBACheck2.App.Services;

namespace DBACheck2.App;

public partial class SettingsWindow:Window
{
    private bool _ready;
    public event Action<AppLanguage>? LanguageChanged;
    public event Action? SubscriptionRequested;
    public event Action? EnterpriseRequested;
    private bool En=>LocalizationService.Current==AppLanguage.En;

    public SettingsWindow()
    {
        InitializeComponent();
        LanguageBox.SelectedIndex=LocalizationService.Current==AppLanguage.En?1:0;
        ApplyLanguage();
        _ready=true;
    }

    private void ApplyLanguage()
    {
        TitleText.Text=En?"SETTINGS":"CONFIGURACIÓN";
        SubtitleText.Text=En?"Application preferences":"Preferencias de la aplicación";
        LanguageTitleText.Text=En?"LANGUAGE":"IDIOMA";
        LanguageHelpText.Text=En?"Changes apply immediately and are remembered.":"Los cambios se aplican inmediatamente y se recuerdan.";
        SubscriptionTitleText.Text=En?"SUBSCRIPTION":"SUSCRIPCIÓN";
        SubscriptionText.Text=En?"Plans, license status and billing.":"Planes, estado de licencia y facturación.";
        SubscriptionButton.Content=En?"MANAGE SUBSCRIPTION":"GESTIONAR SUSCRIPCIÓN";
        EnterpriseButton.Content=En?"ENTERPRISE CENTER":"CENTRO ENTERPRISE";
        FutureTitleText.Text=En?"PREFERENCES":"PREFERENCIAS";
        FutureText.Text=En?"This area is prepared for future UI, startup and update-channel preferences.":"Esta área queda preparada para preferencias futuras de interfaz, inicio y canal de actualizaciones.";
    }

    private void SubscriptionButton_Click(object sender,RoutedEventArgs e)=>SubscriptionRequested?.Invoke();
    private void EnterpriseButton_Click(object sender,RoutedEventArgs e)=>EnterpriseRequested?.Invoke();

    private void LanguageBox_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!_ready)return;
        var lang=LanguageBox.SelectedIndex==1?AppLanguage.En:AppLanguage.Es;
        LocalizationService.Set(lang);
        ApplyLanguage();
        StatusText.Text=lang==AppLanguage.En?"Language updated.":"Idioma actualizado.";
        LanguageChanged?.Invoke(lang);
    }
}
