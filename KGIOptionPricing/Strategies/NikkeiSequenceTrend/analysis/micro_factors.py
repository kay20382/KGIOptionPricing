"""Causal interval flow; unknown aggressor stays unknown, never invented."""
class MicroFrame:
    def __init__(self):self.reset()
    def reset(self):
        self.volume=self.signed_volume=self.count=self.signed_count=0.
        self.depth=self.anchor=None
    def trade(self,price,qty,bid,ask,quote_age):
        if qty<=0:return
        side=0
        if 0<=quote_age<=2 and 0<bid<ask:
            side=1 if price>=ask else -1 if price<=bid else 0
        self.volume+=qty;self.signed_volume+=side*qty;self.count+=1;self.signed_count+=side
    def book(self,bid_qty,ask_qty):
        self.depth=(bid_qty,ask_qty)
        if self.anchor is None:self.anchor=self.depth
    def drain(self):
        change=0.
        if self.depth is not None and self.anchor is not None:
            b,a=self.depth;b0,a0=self.anchor
            change=((b-b0)-(a-a0))/max(b0+a0,1)
        result=[self.signed_volume/max(self.volume,1),self.signed_count/max(self.count,1),change]
        self.volume=self.signed_volume=self.count=self.signed_count=0.;self.anchor=self.depth
        return result