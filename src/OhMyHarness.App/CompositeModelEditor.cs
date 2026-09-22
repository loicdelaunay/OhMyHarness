using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;

namespace OhMyHarness.App;
public sealed partial class MainWindow
{
    StackPanel BuildCompositeModelEditor(ProviderDraft draft,List<ProviderDraft> drafts)
    {
        var panel=new StackPanel{Spacing=12};
        var available=drafts.Where(x=>x.Id>0 && x.Kind!="composite").ToList();
        panel.Children.Add(Label("Choisissez des fournisseurs déjà enregistrés. Les tâches des sous-agents sont lancées en parallèle, puis l’orchestrateur exploite leurs résultats. Les permissions et le mode Plan restent appliqués. / Save providers first. Subagents run in parallel; the orchestrator uses their results.",12));
        var config=string.IsNullOrEmpty(draft.CompositeJson)?new CompositeModel():CompositeModel.Read(draft.CompositeJson);
        void Save()=>draft.CompositeJson=config.Json();
        StackPanel Assignment(AgentModel assignment,string heading)
        {
            var row=new StackPanel{Spacing=8};row.Children.Add(Label(heading,16));
            var provider=new ComboBox{Header="Fournisseur / Provider",ItemsSource=available,SelectedItem=available.FirstOrDefault(x=>x.Id==assignment.ProviderId),HorizontalAlignment=HorizontalAlignment.Stretch};
            var model=new ComboBox{Header="Modèle / Model",IsEditable=true,Text=assignment.Model,HorizontalAlignment=HorizontalAlignment.Stretch};
            void SelectProvider(bool reset)
            {
                if(provider.SelectedItem is not ProviderDraft source)return;
                assignment.ProviderId=source.Id;
                model.ItemsSource=ModelCatalog.GetModelsForProvider(new Provider{Name=source.Name,BaseUrl=source.BaseUrl,Kind=source.Kind,Model=source.Model,DetectedModelsJson=source.DetectedModelsJson,SelectedModelsJson=source.SelectedModelsJson});
                if(reset||string.IsNullOrWhiteSpace(assignment.Model))model.Text=assignment.Model=source.Model;
                Save();
            }
            provider.SelectionChanged+=(_,_)=>SelectProvider(true);
            model.SelectionChanged+=(_,_)=>{if(model.SelectedItem is string selected){assignment.Model=selected;Save();}};
            model.TextSubmitted+=(_,e)=>{assignment.Model=e.Text;Save();};
            model.LostFocus+=(_,_)=>{assignment.Model=model.Text;Save();};
            if(provider.SelectedItem==null)provider.SelectedItem=available.FirstOrDefault();else SelectProvider(false);
            row.Children.Add(provider);row.Children.Add(model);
            var error=Label("",12);
            row.Children.Add(Action("Charger les modèles / Load models",async()=>{
                try{
                    if(provider.SelectedItem is not ProviderDraft target)return;
                    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var source=db.Providers.Local.Single(x=>x.Id==target.Id);var key=KeyVault.Decrypt(source.ProtectedKey);
                    var values=source.IsOpenCode?(await openCodeEngine.ModelsAsync(source,key,null,timeout.Token)).Select(x=>x.Reference).ToList():await engine.ModelsAsync(source,key,timeout.Token);
                    model.ItemsSource=values;model.Text=assignment.Model;error.Text="";
                }catch(Exception ex){error.Text=ex.Message;}
            }));row.Children.Add(error);return row;
        }
        panel.Children.Add(Assignment(config.Orchestrator,"Orchestrateur / Orchestrator"));
        var children=new StackPanel{Spacing=12};panel.Children.Add(children);
        void Render()
        {
            children.Children.Clear();
            foreach(var agent in config.Agents.ToList())
            {
                var item=Assignment(agent,"Sous-agent / Subagent");
                var name=new TextBox{Header="Nom / Name",Text=agent.Name,MaxLength=80};var task=new TextBox{Header="Tâche / Task",Text=agent.Task,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxLength=8000,MinHeight=75};
                name.TextChanged+=(_,_)=>{agent.Name=name.Text;Save();};task.TextChanged+=(_,_)=>{agent.Task=task.Text;Save();};
                item.Children.Add(name);item.Children.Add(task);item.Children.Add(Action("Supprimer / Remove",()=>{config.Agents.Remove(agent);Save();Render();return Task.CompletedTask;}));children.Children.Add(item);
            }
        }
        Render();
        panel.Children.Add(Action("＋ Sous-agent / Subagent",()=>{if(config.Agents.Count<6){config.Agents.Add(new(){Name="Agent "+(config.Agents.Count+1),Task="Analyser les sources utiles à la demande et rapporter les résultats."});Render();Save();}return Task.CompletedTask;}));
        Save();return panel;
    }
}
