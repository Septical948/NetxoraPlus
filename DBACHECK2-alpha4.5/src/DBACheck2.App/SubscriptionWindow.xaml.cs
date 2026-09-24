using System.Windows;
using DBACheck2.App.Models;
using DBACheck2.App.Services;

namespace DBACheck2.App;

public partial class SubscriptionWindow:Window
{
    private readonly SubscriptionService _subscription=new();
    private SubscriptionSnapshot? _snapshot;
    private bool En=>LocalizationService.Current==AppLanguage.En;

    public SubscriptionWindow()
    {
        InitializeComponent();
        ApplyLanguage();
        Loaded+=async(_,__)=>await LoadAsync(false);
    }

    private void ApplyLanguage()
    {
        TitleText.Text=En?"SUBSCRIPTION":"SUSCRIPCIÓN";
        SubtitleText.Text=En?"Plans, license status and billing":"Planes, estado de licencia y facturación";
        CurrentPlanLabel.Text=En?"CURRENT PLAN":"PLAN ACTUAL";
        RefreshButton.Content=En?"REFRESH STATUS":"ACTUALIZAR ESTADO";
        PortalButton.Content=En?"CUSTOMER PORTAL":"PORTAL DE CLIENTE";
        StandardPositionText.Text=En?"Individual DBA toolkit: assess, diagnose and resolve manually.":"Herramienta individual para DBA: evaluar, diagnosticar y resolver manualmente.";
        PlusPositionText.Text=En?"DBA operations platform: detect, prioritize, correlate and diagnose.":"Plataforma operativa DBA: detectar, priorizar, correlacionar y diagnosticar.";
        EnterprisePositionText.Text=En?"Organization-wide DBA operations, governance and commercial support.":"Operación DBA para organizaciones, gobierno y soporte comercial.";
        StandardMonthlyButton.Content=En?"STANDARD MONTHLY":"STANDARD MENSUAL";
        StandardAnnualButton.Content=En?"STANDARD ANNUAL":"STANDARD ANUAL";
        PlusMonthlyButton.Content=En?"PLUS MONTHLY":"PLUS MENSUAL";
        PlusAnnualButton.Content=En?"PLUS ANNUAL":"PLUS ANUAL";
        EnterpriseButton.Content=En?"CONTACT SALES":"CONTACTAR VENTAS";
        EnterprisePriceText.Text=En?"Contact sales":"Consultar";
    }

    private async Task LoadAsync(bool refresh)
    {
        try
        {
            SetBusy(true);
            _snapshot=refresh?await _subscription.RefreshAsync():await _subscription.LoadAsync();
            CurrentPlanText.Text=$"DBACHECK2 {_snapshot.Plan}";
            CurrentStatusText.Text=_snapshot.DevelopmentLicense
                ? (En?"Development license · unrestricted during Beta 2":"Licencia de desarrollo · sin restricciones durante Beta 2")
                : $"{_snapshot.State} · {_snapshot.Cycle}"+(_snapshot.CurrentPeriodEnd.HasValue?$" · until {_snapshot.CurrentPeriodEnd:yyyy-MM-dd}":"");
            BillingStatusText.Text=_subscription.BillingBackendConfigured
                ? (En?"Billing backend configured. Checkout and customer portal are available.":"Backend de facturación configurado. Checkout y portal de cliente disponibles.")
                : (En
                    ?"Billing UI is ready, but no production billing backend is configured yet. No payment secret is stored in the desktop application."
                    :"La interfaz de facturación está lista, pero todavía no hay un backend de cobro productivo configurado. Ningún secreto de pago se almacena en la aplicación de escritorio.");
        }
        catch(Exception ex){BillingStatusText.Text="ERROR: "+ex.Message;}
        finally{SetBusy(false);}
    }

    private async Task CheckoutAsync(SubscriptionPlan plan,BillingCycle cycle)
    {
        try
        {
            SetBusy(true);
            var url=await _subscription.CreateCheckoutAsync(plan,cycle);
            SubscriptionService.OpenExternal(url);
            BillingStatusText.Text=En?"Secure checkout opened in your browser.":"Checkout seguro abierto en el navegador.";
        }
        catch(Exception ex){BillingStatusText.Text="ERROR: "+ex.Message;}
        finally{SetBusy(false);}
    }

    private async void StandardMonthlyButton_Click(object sender,RoutedEventArgs e)=>await CheckoutAsync(SubscriptionPlan.Standard,BillingCycle.Monthly);
    private async void StandardAnnualButton_Click(object sender,RoutedEventArgs e)=>await CheckoutAsync(SubscriptionPlan.Standard,BillingCycle.Annual);
    private async void PlusMonthlyButton_Click(object sender,RoutedEventArgs e)=>await CheckoutAsync(SubscriptionPlan.Plus,BillingCycle.Monthly);
    private async void PlusAnnualButton_Click(object sender,RoutedEventArgs e)=>await CheckoutAsync(SubscriptionPlan.Plus,BillingCycle.Annual);
    private async void RefreshButton_Click(object sender,RoutedEventArgs e)=>await LoadAsync(true);

    private async void PortalButton_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            SetBusy(true);
            var url=await _subscription.CreateCustomerPortalAsync();
            SubscriptionService.OpenExternal(url);
        }
        catch(Exception ex){BillingStatusText.Text="ERROR: "+ex.Message;}
        finally{SetBusy(false);}
    }

    private void EnterpriseButton_Click(object sender,RoutedEventArgs e)
    {
        var url=(Environment.GetEnvironmentVariable("DBACHECK2_SALES_URL")??"").Trim();
        if(Uri.TryCreate(url,UriKind.Absolute,out _))
        {
            SubscriptionService.OpenExternal(url);
            BillingStatusText.Text=En?"Enterprise sales page opened.":"Página comercial Enterprise abierta.";
        }
        else
        {
            BillingStatusText.Text=En
                ?"Enterprise is sales-assisted. Configure DBACHECK2_SALES_URL when the commercial contact page is available."
                :"Enterprise se gestiona con venta asistida. Configurá DBACHECK2_SALES_URL cuando esté disponible la página comercial.";
        }
    }

    private void SetBusy(bool busy)
    {
        RefreshButton.IsEnabled=!busy;
        PortalButton.IsEnabled=!busy;
        StandardMonthlyButton.IsEnabled=!busy;
        StandardAnnualButton.IsEnabled=!busy;
        PlusMonthlyButton.IsEnabled=!busy;
        PlusAnnualButton.IsEnabled=!busy;
        EnterpriseButton.IsEnabled=!busy;
    }
}
