using KGIOptionPricing;
var b = new decimal[10]; var a = new decimal[10]; var bq = new int[10]; var aq = new int[10];
void Apply(char side, string pos, char top, decimal price, int qty) => ForeignDepthBook.Apply(b,bq,a,aq,side,pos,top,price,qty);
void Check(bool condition) { if (!condition) throw new Exception("Book test failed"); }
Apply('B',"1",'0',100,3); Apply('B',"2",'1',98,4); Apply('B',"2",'1',99,5);
Check(b.Take(3).SequenceEqual(new decimal[]{100,99,98}));
Apply('B',"8",'1',99,20); Check(bq[1]==20 && b.Count(p=>p==99)==1);
Apply('S',"1",'0',105,2); Apply('S',"2",'1',107,3); Apply('S',"2",'1',106,4);
Check(a.Take(3).SequenceEqual(new decimal[]{105,106,107}));
Apply('B',"99",'0',99,0); Check(b[1]==98 && !b.Contains(99));
Apply('S',"1",'0',106,9); Check(a[0]==106 && aq[0]==9 && !a.Contains(105));
for(int i=97;i>=80;i--) Apply('B',"",'1',i,1);
Check(b[0]==100 && b[9]==90 && b.Distinct().Count()==10);
Apply('B',"1",'0',0,1); Check(b[0]==100);
Console.WriteLine("7 price-book scenarios passed.");
