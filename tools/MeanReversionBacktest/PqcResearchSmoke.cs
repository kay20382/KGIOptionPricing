namespace MeanReversionBacktest;

internal static class PqcResearchSmoke
{
    public static int Run()
    {
        PqcMicrostructureSmoke.Run();
        var risk = new PqcResearchMetrics();
        foreach (decimal equity in new decimal[] {0, 200, -100, 150, -50}) risk.Observe(equity);
        if (risk.MaximumLoss != 100 || risk.MaximumDrawdown != 300)
            throw new Exception($"Daily loss must start at zero, drawdown at running peak: {risk.MaximumLoss}/{risk.MaximumDrawdown}");
        Console.WriteLine("PASS PQC daily marked maximum loss and peak drawdown are distinct.");
        var at = DateTimeOffset.Parse("2026-10-05T08:00:00+08:00");
        var o = new KGIOptionPricing.StrategyFramework.StrategyDecisionObservation { Timestamp=at,
            CandidateId="candidate-1", PrimaryReasonCode="NeutralDirection", Decision=KGIOptionPricing.StrategyFramework.StrategyDecision.Reject,
            Features=new Dictionary<string,decimal?>{{"price",100}}};
        if (!risk.ShouldRecord(o) || risk.ShouldRecord(o with {Timestamp=at.AddMilliseconds(1),Features=new Dictionary<string,decimal?>{{"price",101}}}) ||
            !risk.ShouldRecord(o with {Timestamp=at.AddMilliseconds(2),PrimaryReasonCode="InsufficientSideDepth"}) ||
            !risk.ShouldRecord(o with {Timestamp=at.AddSeconds(6)})) throw new Exception("Decision compaction lost reason transitions or did not compact changing features.");
        Console.WriteLine("PASS decision compaction preserves transitions and heartbeat, not every feature tick.");
        var broker=new PqcResearchExecution(new KGIOptionPricing.StrategyFramework.Execution.CrossPricePaperExecutionService(36,fillMarketOnBook:true));
        var fills=new List<KGIOptionPricing.StrategyFramework.ExecutionEvent>();broker.Filled+=fills.Add;
        broker.Submit(new KGIOptionPricing.StrategyFramework.PaperOrderRequest{StrategyInstanceId="stop-test",ClientOrderId="stop-1",Symbol="225MC-Z26",
            Type=KGIOptionPricing.StrategyFramework.PaperOrderType.BrokerLimit,Role=KGIOptionPricing.StrategyFramework.PaperOrderRole.StopLoss,
            Side=KGIOptionPricing.OrderManagement.Core.BuySell.Sell,Price=100,Quantity=1},at);
        if(fills.Count!=0)throw new Exception("Research exit must not fabricate immediate fill");
        broker.OnMarketEvent(new KGIOptionPricing.StrategyFramework.MarketEvent{Symbol="225MC-Z26",Timestamp=at.AddSeconds(1),Sequence=1,
            Kind=KGIOptionPricing.StrategyFramework.MarketEventKind.Book,LastPrice=95,BidPrices=[95],AskPrices=[100],BidQuantities=[10],AskQuantities=[10]});
        if(fills.Count!=1 || fills[0].Price!=95 || fills[0].Commission!=36 || fills[0].ClientOrderId!="stop-1")
            throw new Exception("Research market stop must exit at next actual contra book, with original attribution and fee.");
        Console.WriteLine("PASS research stop crosses next actual contra book, not stale requested limit.");
        PqcSessionRegression.Run();
        PqcSessionRegression.Run(existingStop:true);
        string probe=Path.Combine(Path.GetTempPath(),"pqc-research-smoke-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(probe);
        string input=Path.Combine(probe,"events.jsonl"),config=Path.Combine(probe,"profiles.json");
        File.WriteAllText(input,System.Text.Json.JsonSerializer.Serialize(new KGIOptionPricing.StrategyFramework.MarketEvent{
            ProductId="225MC",Symbol="225MC-Z26",Kind=KGIOptionPricing.StrategyFramework.MarketEventKind.Book,Timestamp=at,Sequence=1,LastPrice=1000,
            BidPrices=[1000,995,990],AskPrices=[1005,1010,1015],BidQuantities=[100,100,100],AskQuantities=[100,100,100]},new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))+"\n");
        File.WriteAllText(config,"{\"smoke\":{}}");
        if(PqcResearch.Run(["--pqc-research",input,Path.Combine(probe,"run"),config,"20261005","training"]).GetAwaiter().GetResult()!=0)
            throw new Exception("End-to-end offline runner failed.");
        Console.WriteLine("PASS end-to-end zero-trade run finalizes provenance, summary and validated experiment record.");
        return 0;
    }
}
