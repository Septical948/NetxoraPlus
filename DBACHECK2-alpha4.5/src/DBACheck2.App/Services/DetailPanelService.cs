using System.Windows;
using System.Windows.Controls;

namespace DBACheck2.App.Services;

public static class DetailPanelService
{
    public static void Toggle(RowDefinition row, Button button, double collapsedHeight=170)
    {
        var expanded=row.Height.IsStar;
        row.Height=expanded?new GridLength(collapsedHeight):new GridLength(2,GridUnitType.Star);
        button.Content=expanded
            ? (LocalizationService.Current==AppLanguage.En?"EXPAND":"EXPANDIR")
            : (LocalizationService.Current==AppLanguage.En?"COLLAPSE":"CONTRAER");
    }

    public static void Initialize(RowDefinition row, Button button, double collapsedHeight=170)
    {
        row.Height=new GridLength(collapsedHeight);
        button.Content=LocalizationService.Current==AppLanguage.En?"EXPAND":"EXPANDIR";
    }
}
