using KGIOptionPricing.StrategyFramework;
namespace KGIOptionPricing.Strategies.MultiFactorMeanR17;
static class FactorTests
{
    static void Check(bool passed,string message){if(!passed)throw new Exception(message);}
    public static void Run()
    {
        var t=DateTimeOffset.Parse("2026-09-28T08:00:00+08:00");
        var f=new FactorState();
        MarketEvent Book(string id,DateTimeOffset at,long sequence,decimal bid=100,decimal ask=105)=>new(){ProductId=id,Symbol=id,Timestamp=at,Sequence=sequence,Kind=MarketEventKind.Book,
            BidPrices=new[]{bid,bid-5,bid-10},AskPrices=new[]{ask,ask+5,ask+10},BidQuantities=new[]{30,20,10},AskQuantities=new[]{10,20,30}};
        f.OnMarket(Book("225MC",t,1));
        var s=f.Read("225MC",t,5);
        Check(s!=null && s.QueueImbalance==.5m,"queue imbalance must use volume");
        Check(ReferenceEquals(s,f.Read("225MC",t,5)),"same event/time factor snapshots should be reused without changing values");
        Check(Math.Abs(s!.DepthVariance-5m/9)<.0000001m,"depth dispersion must be variance, not standard deviation");
        f.OnMarket(Book("225MC",t.AddSeconds(1),2,105,110));
        Check(f.Read("225MC",t.AddSeconds(1),5)!.Ofi>0,"bid/ask price increases must give positive observed OFI");
        Check(!ReferenceEquals(s,f.Read("225MC",t.AddSeconds(1),5)),"new events must invalidate factor snapshot cache");
        Check(f.Read("225MC",t.AddSeconds(4),5)==null,"stale book must not become usable");
        f.OnMarket(Book("225MC",t.AddSeconds(.5),3,500,505));
        Check(f.Read("225MC",t.AddSeconds(1),5)!.Mid==107.5m,"late event must not corrupt book");
        f.OnMarket(Book("225MC",t.AddSeconds(2),4));
        Check(f.Read("225M",t.AddSeconds(2),5)==null,"products must remain isolated");
        f.OnMarket(Book("225MC",t.Date.AddHours(16),5));
        Check(f.Read("225MC",t.Date.AddHours(16),5)!.MeanDeviationTicks==null,"session must reset history");
        FactorState.Shared=new();
        void Refresh(DateTimeOffset at,long sequence)
        {foreach(string id in new[]{"225MC","225M","225"})FactorState.Shared.OnMarket(Book(id,at,sequence));}
        var power=new KGIOptionPricing.Strategies.MultiFactorMeanR17.PowerSnapshot(100,0,0,100,100,100,0,true,true,"ready",null,
            Array.Empty<KGIOptionPricing.Strategies.MultiFactorMeanR17.MovementSegment>(),null,null,0,0);
        var signal=new MultiFactorSignal(new Dictionary<string,string>{{"ResearchFactorMode","1"}});
        Refresh(t,1);
        Check(signal.Evaluate(t,power,power,true)==0,"confirmation may not use future samples");
        Check(signal.Evaluate(t.AddMilliseconds(500),power,power,true)==0,"at most one decision in an open second");
        Refresh(t.AddSeconds(1),2);
        Check(signal.Evaluate(t.AddSeconds(1),power,power,true)==1,"confirmed causal flow and book consensus");
        Check(signal.Evaluate(t.AddSeconds(1.5),power,power,false)==0,"data gate overrides factor score");
        Refresh(t.AddSeconds(2),3);
        Check(signal.Evaluate(t.AddSeconds(2),power,power,true)==0,"unhealthy period must reset confirmation");
        Refresh(t.AddSeconds(7),4);
        Check(signal.Evaluate(t.AddSeconds(7),power,power,true)==0,"missing decision frames must not count as confirmed time");
        var range=new MultiFactorSignal(new Dictionary<string,string>{{"ResearchFactorMode","3"}});
        Check(range.Evaluate(t.AddSeconds(2),power,power,true)==0,"unwarmed range feature cannot act as zero or pass");
        var fast=new MultiFactorSignal(new Dictionary<string,string>{{"ResearchFactorMode","1"},{"ResearchDecisionFrameMilliseconds","250"},{"ResearchFactorConfirmSeconds","0"}});
        Refresh(t.AddSeconds(10),10);
        Check(fast.Evaluate(t.AddSeconds(10),power,power,true)==1,"explicit zero confirmation may act only on current healthy known snapshots");
        Check(fast.Evaluate(t.AddSeconds(10.1),power,power,true)==0,"250ms frame must prevent duplicate decisions within the same bucket");
        Refresh(t.AddSeconds(10.25),11);
        Check(fast.Evaluate(t.AddSeconds(10.25),power,power,true)==1,"new 250ms bucket may use newly observed snapshots");
        Check(fast.Evaluate(t.AddSeconds(10.5),power,power,false)==0,"faster timing must preserve external health rejection");
        Check(fast.Evaluate(t.AddSeconds(13),power,power,true)==0,"zero confirmation must reject stale factor books");
        Refresh(t.AddSeconds(14),12);
        Check(fast.Evaluate(t.AddSeconds(13.5),power,power,true)==0,"future factor watermark cannot be used by an earlier decision");
        Check(fast.Evaluate(t.AddSeconds(14),power,power,true)==1,"current causal snapshot remains usable after rejected earlier request");
        bool RejectTiming(string key,string value)
        {try{new MultiFactorSignal(new Dictionary<string,string>{{key,value}});return false;}catch(ArgumentOutOfRangeException){return true;}}
        Check(RejectTiming("ResearchDecisionFrameMilliseconds","0") && RejectTiming("ResearchDecisionFrameMilliseconds","99") && RejectTiming("ResearchDecisionFrameMilliseconds","1001") && RejectTiming("ResearchDecisionFrameMilliseconds","250.5"),"decision frame must be an integer from100 through1000ms");
        Check(RejectTiming("ResearchFactorConfirmSeconds","-1"),"negative confirmation must reject rather than silently act immediately");
        var movements=new KGIOptionPricing.Strategies.MultiFactorMeanR17.MovementTracker(5,2);
        movements.OnPrice(t,100);movements.OnPrice(t.AddSeconds(1),112);movements.OnPrice(t.AddSeconds(2),107);
        var saved=movements.History;
        bool immutable=false;
        try{((IList<KGIOptionPricing.Strategies.MultiFactorMeanR17.MovementSegment>)saved)[0]=saved[0] with {IsCompleted=false};}
        catch(NotSupportedException){immutable=true;}
        Check(immutable,"shared snapshot history must be immutable");
        movements.OnPrice(t.AddSeconds(3),98);movements.OnPrice(t.AddSeconds(4),103);
        Check(saved.Count==1 && movements.History.Count==2 && movements.AverageUp==12 && movements.AverageDown==14,"cached snapshots preserve completed ranges and averages");
        var oldCore=new KGIOptionPricing.Strategies.BuySellPower.PowerIndicator(new(10,10,60,30,20));
        var newCore=new KGIOptionPricing.Strategies.MultiFactorMeanR17.PowerIndicator(new(10,10,60,30,20));
        var random=new Random(225);decimal price=70000;
        for(int i=0;i<5000;i++)
        {
            var at=t.AddMilliseconds(i*310);price+=5*random.Next(-8,9);
            int quantity=new[]{1,10,11,21,0}[random.Next(5)];
            decimal bid=i%3==0?price:price-5,ask=i%3==1?price:price+5;
            oldCore.OnTrade(at,price,quantity,bid,ask);newCore.OnTrade(at,price,quantity,bid,ask);
            if(i%100==0)Check(System.Text.Json.JsonSerializer.Serialize(oldCore.Snapshot())==System.Text.Json.JsonSerializer.Serialize(newCore.Snapshot()),"cached indicator must match frozen baseline over deterministic trades");
        }
        oldCore.Advance(t.AddSeconds(1700));newCore.Advance(t.AddSeconds(1700));
        Check(System.Text.Json.JsonSerializer.Serialize(oldCore.Snapshot())==System.Text.Json.JsonSerializer.Serialize(newCore.Snapshot()),"idle expiry must preserve baseline time-weighted averages");
        var tape=new FactorState();tape.OnMarket(Book("225MC",t,1));
        MarketEvent Trade(DateTimeOffset at,long seq,decimal p,int q)=>new(){ProductId="225MC",Symbol="225MC",Timestamp=at,Sequence=seq,Kind=MarketEventKind.Trade,LastPrice=p,TradeQuantity=q};
        tape.OnMarket(Trade(t,2,100,11));tape.OnMarket(Trade(t.AddSeconds(20),3,110,10));
        tape.OnMarket(Trade(t.AddSeconds(30),4,105,21));tape.OnMarket(Book("225MC",t.AddSeconds(30),5));
        decimal expectedVwap=(110m*10+105m*21)/31;
        Check(tape.Read("225MC",t.AddSeconds(30),5)!.VwapDeviationTicks==(expectedVwap-102.5m)/5,"VWAP must use actual positive prints, volume weights and exact window expiry");
        tape.OnMarket(Book("225MC",t.AddSeconds(60),6));
        Check(tape.Read("225MC",t.AddSeconds(60),5)!.VwapDeviationTicks==null,"empty VWAP window must be missing");
        var five=new FactorState();
        five.OnMarket(Book("225MC",t,1) with {BidPrices=new decimal[]{100,95,90,85,80},AskPrices=new decimal[]{105,110,115,120,125},BidQuantities=new[]{30,20,10,5,5},AskQuantities=new[]{10,20,30,5,5}});
        Check(five.Read("225MC",t,5)!.BestDepthShareDifference==2m/7,"fixed five-level best-depth share must conserve each side's total");
        Check(f.Read("225MC",t.Date.AddHours(16),5)!.BestDepthShareDifference==null,"partial depth cannot stand in for factor51 fixed5");
        var volatility=new FactorState();
        for(int i=0;i<=31;i++)volatility.OnMarket(Book("225MC",t.AddSeconds(i),i+1,100+(i%2)*5,105+(i%2)*5));
        var v=volatility.Read("225MC",t.AddSeconds(31),5)!;
        decimal expectedRv=(decimal)(30*Math.Pow(Math.Log(107.5/102.5),2));
        Check(v.LogRealizedVariance30.HasValue && Math.Abs(v.LogRealizedVariance30.Value-expectedRv)<.0000000001m,"fixed30 RV must sum completed log returns, excluding open second");
        Check(v.DownsideVarianceShare30.HasValue && Math.Abs(v.DownsideVarianceShare30.Value-.5m)<.0000000001m && v.TickVolatility30==1 && v.LastReturnTicks==-1,"volatility and last direction must use completed causal samples");
        var arrivals=new FactorState();var arrival=t;
        for(int i=0;i<=200;i++)
        {
            if(i>0)arrival=arrival.AddSeconds(i%2==1?1:3);
            arrivals.OnMarket(Trade(arrival,i+1,100,11));
        }
        arrivals.OnMarket(Book("225MC",arrival,202));
        Check(arrivals.Read("225MC",arrival,5)!.TradeIntervalCV200==.5m,"population CV must use last200 arrival intervals with actual timestamps");
        Check(volatility.Read("225MC",t.AddSeconds(31),5)!.TradeIntervalCV200==null,"missing trade arrivals must not pass a rhythm filter");
        arrivals.OnMarket(Book("225MC",arrival.AddSeconds(100),203));
        Check(arrivals.Read("225MC",arrival.AddSeconds(100),5)!.TradeIntervalCV200==null,"recording gaps must restart arrival samples");
        FactorState.Shared=new();
        void Rising(int i){foreach(string id in new[]{"225MC","225M","225"})FactorState.Shared.OnMarket(Book(id,t.AddSeconds(i),i+1,100+i*5,105+i*5));}
        for(int i=0;i<=31;i++)Rising(i);
        var trend=new MultiFactorSignal(new Dictionary<string,string>{{"ResearchFactorMode","7"},{"ResearchScoreThreshold","3"},{"ResearchRequireLastReturn","1"},{"ResearchMaxVolatilityTicks","2"}});
        var missingRhythm=new MultiFactorSignal(new Dictionary<string,string>{{"ResearchFactorMode","7"},{"ResearchScoreThreshold","3"},{"ResearchMaxArrivalCV","3"}});
        var missingMinimum=new MultiFactorSignal(new Dictionary<string,string>{{"ResearchFactorMode","7"},{"ResearchScoreThreshold","3"},{"ResearchMinMiniArrivalCV","1.110533"}});
        Check(trend.Evaluate(t.AddSeconds(31),power,power,true)==0,"trend must still require causal confirmation");
        missingRhythm.Evaluate(t.AddSeconds(31),power,power,true);
        missingMinimum.Evaluate(t.AddSeconds(31),power,power,true);
        Rising(32);
        Check(trend.Evaluate(t.AddSeconds(32),power,power,true)==1,"completed trend and volatility filters must combine without future frames");
        Check(missingRhythm.Evaluate(t.AddSeconds(32),power,power,true)==0,"missing arrival samples must reject a rhythm-filtered signal");
        Check(missingMinimum.Evaluate(t.AddSeconds(32),power,power,true)==0,"minimum-arrival-CV filter cannot pass absent raw-print samples");
        var risk=ResearchRiskDistances.Calculate(1,5,2,1.5m);
        Check(risk.HasValue && risk.Value.Stop==55 && risk.Value.Target==85,"volatility risk must round up to actual ticks using past30 volatility");
        risk=ResearchRiskDistances.Calculate(100,5,2,1.5m);
        Check(risk.HasValue && risk.Value.Stop==150 && risk.Value.Target==225,"volatility stop must be capped at30 ticks independent of volatility");
        Check(ResearchRiskDistances.Calculate(null,5,2,1.5m)==null && ResearchRiskDistances.Calculate(0,5,2,1.5m)==null,"unwarmed or stationary volatility must not manufacture risk distances");
        bool invalidRisk=false;
        try{ResearchRiskDistances.Calculate(1,5,2,1.5m,maxStopTicks:31);}catch(ArgumentOutOfRangeException){invalidRisk=true;}
        Check(invalidRisk,"risk helper must reject a configured cap above150points");
        
        FactorState.Shared=new();
        var missingRate=new MultiFactorSignal(new Dictionary<string,string>{{"ResearchFactorMode","7"},{"ResearchScoreThreshold","3"},{"ResearchMinMiniPrintRate","2"}});
        for(int i=0;i<=31;i++)Rising(i);
        missingRate.Evaluate(t.AddSeconds(31),power,power,true);Rising(32);
        Check(missingRate.Evaluate(t.AddSeconds(32),power,power,true)==0,"Mini rate minimum must reject absent positive prints, without substituting other products");
        FactorState.Shared=new();long seq=0;
        void CounterTrendFrame(int i)
        {
            var at=t.AddSeconds(i);
            foreach(string id in new[]{"225MC","225M","225"})
            {
                decimal price=id=="225M"?300-i*5:100+i*5;
                FactorState.Shared.OnMarket(Book(id,at,++seq,price,price+5));
                if(id=="225M")for(int j=0;j<3;j++)FactorState.Shared.OnMarket(new MarketEvent{ProductId=id,Symbol=id,Timestamp=at,Sequence=++seq,Kind=MarketEventKind.Trade,LastPrice=price,TradeQuantity=11});
            }
        }
        for(int i=0;i<=31;i++)CounterTrendFrame(i);
        var activeCounterTrend=new MultiFactorSignal(new Dictionary<string,string>{{"ResearchFactorMode","7"},{"ResearchScoreThreshold","3"},{"ResearchMinMiniPrintRate","2"},{"ResearchUseMiniMomentumCeiling","1"},{"ResearchMaxSignedMiniMomentumTicks","-3"}});
        var insufficientRate=new MultiFactorSignal(new Dictionary<string,string>{{"ResearchFactorMode","7"},{"ResearchScoreThreshold","3"},{"ResearchMinMiniPrintRate","4"}});
        var insufficientCounterTrend=new MultiFactorSignal(new Dictionary<string,string>{{"ResearchFactorMode","7"},{"ResearchScoreThreshold","3"},{"ResearchUseMiniMomentumCeiling","1"},{"ResearchMaxSignedMiniMomentumTicks","-40"}});
        activeCounterTrend.Evaluate(t.AddSeconds(31),power,power,true);
        insufficientRate.Evaluate(t.AddSeconds(31),power,power,true);
        insufficientCounterTrend.Evaluate(t.AddSeconds(31),power,power,true);
        CounterTrendFrame(32);
        Check(activeCounterTrend.Evaluate(t.AddSeconds(32),power,power,true)==1,"Mini activity and completed opposite momentum can confirm together");
        Check(insufficientRate.Evaluate(t.AddSeconds(32),power,power,true)==0,"rate above observed warm window must reject");
        Check(insufficientCounterTrend.Evaluate(t.AddSeconds(32),power,power,true)==0,"directional Mini momentum bound must reject a weaker opposite move");
        
        var cacheCore=new KGIOptionPricing.Strategies.MultiFactorMeanR17.PowerIndicator(new KGIOptionPricing.Strategies.MultiFactorMeanR17.PowerParameters(10,10,60,30,20));
        var referenceCore=new KGIOptionPricing.Strategies.BuySellPower.PowerIndicator(new KGIOptionPricing.Strategies.BuySellPower.PowerParameters(10,10,60,30,20));
        cacheCore.OnTrade(t,105,11,100,105);referenceCore.OnTrade(t,105,11,100,105);
        int initialCalculations=cacheCore.PercentageCalculationCount;
        for(int i=1;i<=1000;i++)
        {
            var at=t.AddMilliseconds(i);cacheCore.Advance(at);referenceCore.Advance(at);
            if(i%100==0)Check(System.Text.Json.JsonSerializer.Serialize(cacheCore.Snapshot())==System.Text.Json.JsonSerializer.Serialize(referenceCore.Snapshot()),"cached power cannot skip time-weighted area updates during no new trades");
        }
        Check(cacheCore.PercentageCalculationCount==initialCalculations,"unchanged quantities must not recalculate percentages on every quote or snapshot");
        cacheCore.Advance(t.AddSeconds(10));referenceCore.Advance(t.AddSeconds(10));
        Check(cacheCore.PercentageCalculationCount==initialCalculations+1 && cacheCore.Snapshot().BuyPower==null,"exact expiry must invalidate cached power, including a now empty window");
        Check(System.Text.Json.JsonSerializer.Serialize(cacheCore.Snapshot())==System.Text.Json.JsonSerializer.Serialize(referenceCore.Snapshot()),"exact expiry must preserve all frozen indicator fields");
        cacheCore.OnTrade(t.AddSeconds(11),100,21,100,105);referenceCore.OnTrade(t.AddSeconds(11),100,21,100,105);
        Check(cacheCore.Snapshot().BuyPower==-100 && System.Text.Json.JsonSerializer.Serialize(cacheCore.Snapshot())==System.Text.Json.JsonSerializer.Serialize(referenceCore.Snapshot()),"new valid trade must invalidate the cached percentage and weighted state");
        var exitParameters=R17BaseParameters.Parse(new Dictionary<string,string>{{"TrailingExitMode","1"},{"TrailingActivateRisk","1"},{"TrailingDistanceRisk","0.5"},{"ExitConfirmSeconds","10"},{"PowerExitMode","1"}});
        var exitPolicy=new IndicatorExitPolicy(exitParameters);
        exitPolicy.Reset(1000,950);
        Check(!exitPolicy.TrailHit(1,1045,5,true) && exitPolicy.TrailTrigger==null,"trailing must wait for actual executable 1R gain");
        Check(!exitPolicy.TrailHit(1,1050,5,true) && exitPolicy.TrailTrigger==1025,"1R activation must establish 0.5R retracement on a real tick");
        Check(!exitPolicy.TrailHit(1,1040,5,true) && exitPolicy.TrailTrigger==1025,"retracement must not loosen established protection");
        Check(exitPolicy.TrailHit(1,1025,5,true),"exact trailing trigger must exit");
        exitPolicy.Reset(1000,1050);
        Check(!exitPolicy.TrailHit(-1,950,5,true) && exitPolicy.TrailTrigger==975 && exitPolicy.TrailHit(-1,975,5,true),"short trailing direction must mirror long without changing sign");
        exitPolicy.Reset(1000,950);
        Check(!exitPolicy.TrailHit(1,1100,5,false) && exitPolicy.TrailTrigger==null,"stale quotes cannot create a favorable trailing extreme");
        var opposing=power with {BuyPower=-50,ShortAverage=-10,BaisPower=-5};
        Check(!exitPolicy.PowerDue(t,1,opposing,power,true,true) && exitPolicy.PowerSince==null,"both-product mode must not use Micro reversal alone");
        Check(!exitPolicy.PowerDue(t,1,opposing,opposing,true,true),"opposite confirmation must start before triggering");
        Check(!exitPolicy.PowerDue(t.AddSeconds(9),1,opposing,opposing,true,true) && exitPolicy.PowerDue(t.AddSeconds(10),1,opposing,opposing,true,true),"both-product reversal must honor exact elapsed confirmation");
        Check(!exitPolicy.PowerDue(t.AddSeconds(11),1,opposing,opposing,true,false) && exitPolicy.PowerSince==null,"stale Mini data must reset both-product exit confirmation");
        FactorState.Shared=new();
        void VarianceBook(int i)
        {foreach(string id in new[]{"225MC","225M","225"})FactorState.Shared.OnMarket(new MarketEvent{ProductId=id,Symbol=id,Timestamp=t.AddSeconds(200+i),Sequence=i+1,Kind=MarketEventKind.Book,BidPrices=new decimal[]{70000+10*i,69990+10*i,69980+10*i},AskPrices=new decimal[]{70010+10*i,70020+10*i,70030+10*i},BidQuantities=new[]{30,20,10},AskQuantities=new[]{10,20,30}});}
        var varianceGate=new Dictionary<string,string>{{"ResearchFactorMode","1"},{"ResearchQueueFloor","-1"},{"ResearchUseLargeVariancePressure","1"},{"ResearchMinLargeVariancePressure","0"}};
        var bullishVariance=new MultiFactorSignal(varianceGate);
        var bearishVariance=new MultiFactorSignal(varianceGate);
        var coldVariance=new MultiFactorSignal(varianceGate);
        VarianceBook(0);Check(coldVariance.Evaluate(t.AddSeconds(200),power,power,true)==0,"variance gate needs completed samples before confirmation");
        VarianceBook(1);Check(coldVariance.Evaluate(t.AddSeconds(201),power,power,true)==0,"unwarmed variance may not be silently zero or pass");
        for(int i=2;i<=31;i++)VarianceBook(i);
        Check(bullishVariance.Evaluate(t.AddSeconds(231),power,power,true)==0 && bearishVariance.Evaluate(t.AddSeconds(231),opposing,opposing,true)==0,"variance-gated direction still honors confirmation");
        VarianceBook(32);
        Check(bullishVariance.Evaluate(t.AddSeconds(232),power,power,true)==1,"positive completed variance pressure supports long direction");
        Check(bearishVariance.Evaluate(t.AddSeconds(232),opposing,opposing,true)==0,"upward variance pressure must reject short direction");
        FactorState.Shared=new();
        for(int i=0;i<=31;i++)
            foreach(string id in new[]{"225MC","225M","225"})FactorState.Shared.OnMarket(new MarketEvent{ProductId=id,Symbol=id,Timestamp=t.AddSeconds(300+i),Sequence=i+1,Kind=MarketEventKind.Book,BidPrices=new decimal[]{70000-10*i,69990-10*i,69980-10*i},AskPrices=new decimal[]{70010-10*i,70020-10*i,70030-10*i},BidQuantities=new[]{30,20,10},AskQuantities=new[]{10,20,30}});
        var fallingVariance=new MultiFactorSignal(new Dictionary<string,string>(varianceGate){{"ResearchFactorConfirmSeconds","0"}});
        Check(fallingVariance.Evaluate(t.AddSeconds(331),opposing,opposing,true)==-1,"downward variance pressure must symmetrically support short direction");
        Check(RejectTiming("ResearchMinLargeVariancePressure","1.01") && RejectTiming("ResearchMinLargeVariancePressure","-1.01"),"variance floor cannot be outside its mathematical range");
        var localVariance=new FactorState();
        for(int i=0;i<=51;i++)if(i!=11)localVariance.OnMarket(Book("225MC",t.AddSeconds(400+i),i+1,100+(i%2)*5,105+(i%2)*5));
        var recentVariance=localVariance.Read("225MC",t.AddSeconds(451),5)!;
        Check(recentVariance.MeanDeviationTicks==null,"longer history gap must continue to reject variable-window mean without interpolation");
        Check(recentVariance.LogRealizedVariance30.HasValue && Math.Abs(recentVariance.LogRealizedVariance30.Value-expectedRv)<.0000000001m && recentVariance.DownsideVarianceShare30.HasValue && Math.Abs(recentVariance.DownsideVarianceShare30.Value-.5m)<.0000000001m && recentVariance.TickVolatility30==1,"fixed30 variance validity must depend only on last31 completed consecutive samples");
        var missingRecentVariance=new FactorState();
        for(int i=0;i<=51;i++)if(i!=40)missingRecentVariance.OnMarket(Book("225MC",t.AddSeconds(500+i),i+1,100+(i%2)*5,105+(i%2)*5));
        Check(missingRecentVariance.Read("225MC",t.AddSeconds(551),5)!.LogRealizedVariance30==null,"gap inside fixed30 window must reject rather than interpolate");
        Check(localVariance.Read("225MC",t.AddSeconds(455),5)==null,"fixed-window correction must not relax stale book rejection");
        MiniReturnGateTests.Run();

        Console.WriteLine("PASS FactorTests");
    }
}
