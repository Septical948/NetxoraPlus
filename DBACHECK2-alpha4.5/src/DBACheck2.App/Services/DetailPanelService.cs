using System.Windows;
using System.Windows.Controls;

namespace DBACheck2.App.Services;

public static class DetailPanelService
{
    // Standard DBACHECK detail behavior:
    // collapsed -> findings grid uses the available workspace, detail stays compact.
    // expanded  -> findings grid contracts to a small context strip and detail takes the workspace.
    public static void Toggle(RowDefinition listRow,RowDefinition detailRow,Button button,double collapsedDetailHeight=125,double expandedListHeight=105)
    {
        var expanded=detailRow.Height.IsStar;
        if(expanded)
        {
            listRow.Height=new GridLength(1,GridUnitType.Star);
            detailRow.Height=new GridLength(collapsedDetailHeight);
            button.Content=LocalizationService.Current==AppLanguage.En?"EXPAND":"EXPANDIR";
        }
        else
        {
            listRow.Height=new GridLength(expandedListHeight);
            detailRow.Height=new GridLength(1,GridUnitType.Star);
            button.Content=LocalizationService.Current==AppLanguage.En?"COLLAPSE":"CONTRAER";
        }
    }

    public static void Initialize(RowDefinition listRow,RowDefinition detailRow,Button button,double collapsedDetailHeight=125)
    {
        listRow.Height=new GridLength(1,GridUnitType.Star);
        detailRow.Height=new GridLength(collapsedDetailHeight);
        button.Content=LocalizationService.Current==AppLanguage.En?"EXPAND":"EXPANDIR";
    }

    // Backward-compatible overload for any remaining view not yet migrated.
    public static void Toggle(RowDefinition row,Button button,double collapsedHeight=170)
    {
        var expanded=row.Height.IsStar;
        row.Height=expanded?new GridLength(collapsedHeight):new GridLength(2,GridUnitType.Star);
        button.Content=expanded
            ? (LocalizationService.Current==AppLanguage.En?"EXPAND":"EXPANDIR")
            : (LocalizationService.Current==AppLanguage.En?"COLLAPSE":"CONTRAER");
    }

    public static void Initialize(RowDefinition row,Button button,double collapsedHeight=170)
    {
        row.Height=new GridLength(collapsedHeight);
        button.Content=LocalizationService.Current==AppLanguage.En?"EXPAND":"EXPANDIR";
    }
}
