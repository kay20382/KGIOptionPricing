using KGIOptionPricing;
using System.Reflection;

// Test the deterministic removal primitive without a live API connection.
var method = typeof(KGIQuoteService.MarketDataStruct).GetMethod("RemoveDepthPrice", BindingFlags.NonPublic | BindingFlags.Static)!;
void Check(decimal[] prices, int[] qty, decimal remove, decimal[] expected, int[] expectedQty)
{
    method.Invoke(null, new object[] { prices, qty, remove });
    if (!prices.SequenceEqual(expected) || !qty.SequenceEqual(expectedQty)) throw new Exception("Removal mismatch");
}
Check(new decimal[] { 105, 104, 103 }, new[] { 1, 2, 3 }, 105, new decimal[] { 104, 103, 0 }, new[] { 2, 3, 0 });
Check(new decimal[] { 105, 104, 103 }, new[] { 1, 2, 3 }, 104, new decimal[] { 105, 103, 0 }, new[] { 1, 3, 0 });
Check(new decimal[] { 105, 104, 103 }, new[] { 1, 2, 3 }, 103, new decimal[] { 105, 104, 0 }, new[] { 1, 2, 0 });
Check(new decimal[] { 105, 104, 103 }, new[] { 1, 2, 3 }, 102, new decimal[] { 105, 104, 103 }, new[] { 1, 2, 3 });
Console.WriteLine("4 removal tests passed; packet replay awaits recorded API messages.");
