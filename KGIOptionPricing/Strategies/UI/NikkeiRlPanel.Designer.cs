namespace KGIOptionPricing;

public sealed partial class NikkeiRlPanel
{
    public ComboBox ModelSelector {get;}=new(){Name="nikkeiModelSelector",Width=440,DropDownStyle=ComboBoxStyle.DropDownList};
    readonly FlowLayoutPanel modelSelectionRow=new(){Dock=DockStyle.Top,Height=34,WrapContents=false};
    public string SelectedPolicyVariant=>fixedPolicyVariant ?? (ModelSelector.SelectedIndex switch {1=>"grid15-positive-net",2=>"grid15-q90-research",_=>"earlier"});
    void InitializeModelSelector()
    {
        ModelSelector.Items.AddRange(["目前模型：earlier RL／seed22","第一名：Grid15 Positive Net／門檻0 JPY","第二名：Grid15 Q90／研究門檻-29.20 JPY"]);
        ModelSelector.SelectedIndex=0;
        modelSelectionRow.Controls.Add(new Label{Text="新實例模型",AutoSize=true,Padding=new Padding(0,6,0,0)});
        modelSelectionRow.Controls.Add(ModelSelector);
        ModelSelector.SelectedIndexChanged+=(_,_)=>{
            if(fixedPolicyVariant!=null && ModelSelector.SelectedIndex!=PolicyIndex(fixedPolicyVariant)){
                ModelSelector.SelectedIndex=PolicyIndex(fixedPolicyVariant);return;
            }
            parameterInfo.Text=ParameterText(liveMode);
            Confirm.Checked=false;
            Status.Text="新實例將使用："+SelectedPolicyVariant+"；已啟動實例的模型維持固定。";
        };
    }
}
