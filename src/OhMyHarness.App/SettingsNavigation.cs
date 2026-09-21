using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OhMyHarness.App;

public sealed class SettingsNavigation : Grid
{
    readonly ListBox navigation=new(){MinWidth=150,MaxWidth=220,HorizontalAlignment=HorizontalAlignment.Stretch};
    readonly ScrollViewer body=new(){HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(20,0,4,0)};
    readonly List<UIElement> pages=[];
    public int SelectedIndex {get=>navigation.SelectedIndex;set=>navigation.SelectedIndex=value;}
    public SettingsNavigation()
    {
        ColumnDefinitions.Add(new(){Width=new GridLength(190)});ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        Children.Add(navigation);SetColumn(body,1);Children.Add(body);
        navigation.SelectionChanged+=(_,_)=>{if(SelectedIndex>=0){body.Content=pages[SelectedIndex];body.ChangeView(null,0,null);}};
        SizeChanged+=(_,e)=>ColumnDefinitions[0].Width=new GridLength(e.NewSize.Width<650?150:190);
    }
    public void Add(string title,UIElement page)
    {
        pages.Add(page);navigation.Items.Add(title);if(pages.Count==1)SelectedIndex=0;
    }
}
