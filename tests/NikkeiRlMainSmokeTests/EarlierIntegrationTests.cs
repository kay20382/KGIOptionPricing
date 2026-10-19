using KGIOptionPricing;
using KGIOptionPricing.Strategies.NikkeiRlDirection;
using KGIOptionPricing.StrategyFramework;

static class EarlierIntegrationTests
{
    public static void Run()
    {
        var errors=new List<string>();int checks=0;
        void Check(bool ok,string reason){checks++;if(!ok)errors.Add(reason);}
        var p=NikkeiRlParameters.Parse(new Dictionary<string,string>());
        Check(p.ToDictionary()["ModelId"]=="pruned-earlier-seed22","earlier must be the default model");
        Check(p.ToDictionary()["ResearchReadinessProfile"]=="exclude-flow","parameters must disclose excluded-flow readiness");
        Check(!p.LiveTradingEnabled,"simulation defaults cannot opt into broker orders");
        var at=new DateTimeOffset(2026,10,13,8,0,0,TimeSpan.FromHours(8));
        MarketEvent Book(string product,int sec)=>new(){ProductId=product,Symbol=product,Kind=MarketEventKind.Book,Timestamp=at.AddSeconds(sec),BidPrices=[40000,39995,39990,39985,39980],AskPrices=[40005,40010,40015,40020,40025],BidQuantities=[10,10,10,10,10],AskQuantities=[10,10,10,10,10]};
        MarketEvent Trade(string product,int sec)=>new(){ProductId=product,Symbol=product,Kind=MarketEventKind.Trade,Timestamp=at.AddSeconds(sec).AddMilliseconds(1),LastPrice=40005,TradeQuantity=1};
        var stream=new NikkeiRlFeatureStream(5);RlMarketSnapshot? frame=null;
        for(int s=0;s<=240;s++){
            if(s%3==0){stream.Accept(Book("225MC",s));stream.Accept(Book("225M",s));}
            stream.Accept(Trade("225MC",s));stream.Accept(Trade("225M",s));
            if(s%3==0)frame=stream.Emit(at.AddSeconds(s+1));
        }
        Check(frame!.Valid,"sparse flow must not block a healthy pruned actor after price/power warmup");
        Check(!stream.Emit(at.AddSeconds(244)).Valid,"pruning cannot waive stale books");
        var bad=Book("225MC",244) with{BidPrices=[40000],AskPrices=[40005],BidQuantities=[10],AskQuantities=[10]};
        stream.Accept(bad);stream.Accept(Book("225M",244));stream.Accept(Trade("225MC",244));stream.Accept(Trade("225M",244));
        Check(!stream.Emit(at.AddSeconds(245)).Valid,"pruning cannot waive missing five-level depth");
        var cold=new NikkeiRlFeatureStream(5);cold.Accept(Book("225MC",0));cold.Accept(Book("225M",0));cold.Accept(Trade("225MC",0));cold.Accept(Trade("225M",0));
        Check(!cold.Emit(at.AddSeconds(1)).Valid,"pruning cannot waive price/power warmup");
        var definition=new StrategyDefinition{StrategyType=NikkeiRlFactory.TypeName,InstanceId="earlier-test",Universe=new(){Tradable=new("225MC"),References=[new("225M")]}};
        var strategy=new NikkeiRlStrategy(definition);
        Check(strategy.GetSnapshot().Diagnostics["Readiness"]=="exclude-flow","snapshot must expose actual pruned readiness");
        Check(!LiveStrategyService.TryValidateResearchStrategy(definition,out _),"missing live opt-in must reject");
        using(var panel=new NikkeiRlPanel()){
            var labels=panel.Controls.OfType<System.Windows.Forms.Label>();
            Check(labels.Any(l=>l.Text.Contains("pruned-earlier")&&l.Text.Contains("exclude-flow")),"UI must identify earlier and its readiness");
            panel.ConfigureLive(true);
            Check(panel.StartButton.Enabled&&panel.Confirm.Visible,"manual live controls must be available");
            Check(panel.CreateDefinition().Parameters["ModelId"]=="pruned-earlier-seed22","live panel must select earlier");
            Check(LiveStrategyService.TryValidateResearchStrategy(panel.CreateDefinition(),out _),"explicit earlier live opt-in must pass");
            panel.ConfigureLive(false);
            Check(!LiveStrategyService.TryValidateResearchStrategy(panel.CreateDefinition(),out _),"switching back to simulation removes live opt-in");
        }
        if(errors.Count>0)throw new Exception(string.Join("\n",errors));
        using(var actor=new FrozenRlMainPolicy(p)){
            var baseline=actor.Predict(new double[60],[true,true,true,true,true]);
            for(int index=48;index<=53;index++){
                var obs=new double[60];obs[index]=10000;
                var candidate=actor.Predict(obs,[true,true,true,true,true]);
                Check(candidate.Action==baseline.Action&&candidate.Probabilities.Zip(baseline.Probabilities).All(x=>Math.Abs(x.First-x.Second)<1e-6),"excluded flow column affects frozen ONNX: "+index);
            }
        }
        if(errors.Count>0)throw new Exception(string.Join("\n",errors));
        Console.WriteLine($"PASS earlier integration {checks} checks: model/readiness, health, UI, live opt-in and ONNX flow mask");
    }
}
