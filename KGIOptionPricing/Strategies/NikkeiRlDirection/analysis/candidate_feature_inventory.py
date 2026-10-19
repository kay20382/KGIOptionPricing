"""Explicit feasibility and provenance; no claim of 50 independent alpha signals."""
import csv,json
from pathlib import Path
from observation_schema import RESEARCH_NAMES
OFI='https://arxiv.org/abs/1011.6402'
QI='https://arxiv.org/abs/1512.03492'
CME='https://www.cmegroup.com/education/articles-and-reports/understanding-the-cme-liquidity-tool-methodology'
QR='https://arxiv.org/abs/1312.0563'
TITLES=['買賣價差','雙邊可見總深度','最佳檔佇列不平衡','校準微價格偏移','最佳檔後方價格缺口不對稱','累積深度價格衝擊斜率','深度曲線凸性','固定口數往返掃單成本','買賣兩側衝擊成本偏斜','流動性重心距離不對稱','流動性空間分散度','最大掛單量所在位置不對稱','最佳檔委託筆數不平衡','最佳檔個別委託大小集中度','最佳檔委託流失衡OFI','主動成交量失衡','主動成交事件到達強度','限價新增量方向壓力','撤單危險率不對稱','撤補單量比率','成交間隔變異係數','Hawkes自激成交方向壓力','成交方向自相關','當前同向成交連續長度','成交尺寸集中度','主動掃單跨價位寬度','成交後同價補量吸收率','已確認冰山隱藏成交量壓力','中間價動能','價格路徑效率','短期報酬自相關','已實現變異','下行半變異占比','跳躍變異占比','絕對報酬持續性','已實現偏態','股指期貨公平價偏離','跨期價差異常','領先商品報酬傳導','最佳價佇列耗盡危險率','條件限價成交機率','截尾等待時間','成交後逆向選擇成本','ACD標準化成交間隔','去季節性事件活躍度','價差恢復半衰期','衝擊後深度回補率','狀態條件佇列淨增速','狀態條件撤單強度','短命限價單占比']
IDS=[2,4,5,9,10,11,12,7,15,16,17,23,24,25,29,30,32,33,31,35]
FORMULAS=[
 'log(1+sum(B_i+A_i)), i=1..5',
 '((ask1*B1+bid1*A1)/(B1+A1)-mid)/tick；未校準的weighted-mid代理',
 '(askGap-bidGap)/(askGap+bidGap), gap=最佳至次佳距離/tick',
 '(sellCost5-buyCost5)/(sellCost5+buyCost5)；五檔可見5口成交均價距中間價/tick；兩側深度不足5口拒絕',
 '(askCentroid-bidCentroid)/(askCentroid+bidCentroid)；距mid/tick的數量加權重心',
 'sqrt((sum B_i*(dB_i-cB)^2+sum A_i*(dA_i-cA)^2)/(sum B_i+A_i))',
 '(argmax_i A_i-argmax_i B_i)/4；同量取最近档',
 '(B1-2B2+2B3-2B4+B5)/sum B-(A1-2A2+2A3-2A4+A5)/sum A；深度索引曲率代理',
 'sum_last5s e_n/max(1,(B1_n+A1_n+B1_prev+A1_prev)/2)；e_n為原OFI最佳價更新式',
 'sum_last5s sign_n*qty_n / sum_last5s qty_n；sign先看決策前2秒有效報價，再用tick-rule含同價carry',
 'log(1+count_last5s/5)；所有正量成交事件強度，非已確認aggressor分類',
 'Pearson(sign_n,sign_{n+1}) over last30s；至少4筆，零變異為0',
 'lastSign*min(consecutiveSameSignTrades,100)；過去30秒無成交則0',
 'sum_last30s qty_n^2/(sum_last30s qty_n)^2；成交事件尺寸，非逐筆委託尺寸',
 '(mid_t-mid_{t-15s})/tick；3秒決策前有效報價中間價',
 '(mid_t-mid_{t-30s})/sum abs(mid_k-mid_{k-1})；保留方向',
 'log(1+sum_last30s ((mid_k-mid_{k-1})/tick)^2)；3秒離散tick變化平方和，非tick-by-tick logreturn年化變異',
 'sum r_k^2*1[r_k<0]/sum r_k^2；r為3秒mid tick變化，常數行情為0',
 'Pearson(r_k,r_{k+1})；3秒mid tick變化，至少4筆，零變異為0',
 'Pearson(abs(r_k),abs(r_{k+1}))；3秒mid tick變化，至少4筆，零變異為0']

def inventory():
    selected={i:n for i,n in zip(IDS,RESEARCH_NAMES)};rows=[]
    missing={13:'無逐筆委託筆數',14:'無逐筆委託識別碼及個別委託尺寸',18:'快照淨變化無法分開新增、成交及撤單',19:'無獨立撤單事件及暴露佇列',20:'無獨立撤單／補單事件',26:'無成交群組或aggressor識別，不能確認掃單',28:'無逐筆委託／隱藏量確認欄位',37:'缺現貨指數、利率、股息與到期資料',38:'目前每商品只有同一到期合約，缺跨期',49:'無獨立撤單事件',50:'無逐筆委託生命周期'}
    deferred={6:'五檔可算，與掃單成本高度相關；本輪先用成本偏斜',8:'五檔可算，本輪以5口兩側成本偏斜測試，不另加入總成本',21:'可由成交時間計算；下一輪與事件強度比較',22:'需train-only Hawkes擬合及穩定性驗證',27:'僅可建立成交後快照補量代理，無法確認同單吸收',34:'3秒／30秒僅10個增量；跳躍分解樣本不足，本輪暫緩',36:'30秒僅10個增量，高階矩不穩定，本輪暫緩',39:'225M與225MC可算接收時間傳導；既有兩商品報酬已保留，大型225禁止',40:'需train-only條件事件模型；不得把已發生耗盡作當前預測',41:'成交結果是標籤；僅可train-only預測概率，直接使用未來結果會洩漏',42:'需右截尾存活模型；直接使用當次未來等待時間會洩漏',43:'當次成交後成本是結果／標籤；當前特徵只能使用過去可得估計',44:'需train-only ACD擬合，接收時間非交易所成交時間',45:'需更多完整日估計分時季節性；只能train-only擬合',46:'需明確衝擊事件、右截尾與train-only恢復模型',47:'五檔可建立回補代理；須區分價位切換與實際補單',48:'五檔可算净变化；需固定價位與狀態條件，避免誤稱新增委託'}
    for i,title in enumerate(TITLES,1):
        if i in selected:status='本輪新增';note='工程定義／代理，未宣稱該公式經原論文驗證' if i not in [15] else '原最佳價OFI式；每事件深度正規化是工程延伸'
        elif i in [1,3]:status='既有特徵';note='兩商品spread/obi1既有輸入保留'
        elif i in missing:status='缺資料排除';note=missing[i]
        else:status='可計算或需估計，暫緩';note=deferred[i]
        source=OFI if i in [15,18,19,20] else QI if i in [3,4,13,14] else QR if i in [40,41,42,46,47,48,49,50] else CME if i<=12 else ''
        rows.append(dict(candidateId=i,name=title,status=status,implementedName=selected.get(i,''),note=note,contextSource=source))
    return rows

def write_inventory(output):
    out=Path(output);out.mkdir(parents=True,exist_ok=True)
    rows=inventory()
    with (out/'candidate-feasibility.csv').open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.DictWriter(f,fieldnames=list(rows[0]));writer.writeheader();writer.writerows(rows)
    formulas=[dict(observationIndex=40+j,group='book' if j<8 else 'flow' if j<14 else 'price',candidateId=IDS[j],name=n,formula=FORMULAS[j],attribution='工程定義；contextSource僅為相關概念，非逐項原文公式',contextSource=rows[IDS[j]-1]['contextSource']) for j,n in enumerate(RESEARCH_NAMES)]
    with (out/'feature-formulas.csv').open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.DictWriter(f,fieldnames=list(formulas[0]));writer.writeheader();writer.writerows(formulas)
    (out/'feature-inventory.json').write_text(json.dumps(dict(candidates=rows,implemented=formulas),ensure_ascii=False,indent=2),encoding='utf-8')
    lines=['# 候選特徵可行性與公式','', '資料：225MC及225M逐筆成交、五檔價格／聚合口數，Timestamp為接收時間；無逐筆委託ID、委託筆數、独立撤單、隱藏量確認或交易所同步時鐘。禁止大型225。','',
       '20項新增特徵為8/6/6分組；既有40維保持。weighted-mid不是已校準microprice；OFI是快照轉換估計，未觀測中間事件不能還原。OFI最佳檔anchor與五檔形狀獨立；真正最佳檔無效或相隔超過2秒時中斷OFI，恢復完整5秒觀測才允許新進場。成交方向是報價與tick-rule估計。所有新增特徵僅使用決策前事件；price按3秒有效mid，完整30秒11點才允許新進場，過期報價不補出假路徑。所有五組用相同新增資料健康門檻，因此舊40維結果僅作歷史參考。','',
       '來源只支持概念，不代表50項公式都由該來源提出：[OFI](https://arxiv.org/abs/1011.6402)、[Queue imbalance](https://arxiv.org/abs/1512.03492)、[Queue-reactive](https://arxiv.org/abs/1312.0563)、[CME liquidity methodology](https://www.cmegroup.com/education/articles-and-reports/understanding-the-cme-liquidity-tool-methodology)。','', '|編號|候選|處理|原因|','|---:|---|---|---|']
    lines += [f"|{x['candidateId']}|{x['name']}|{x['status']}|{x['note']}|" for x in rows]
    lines+=['','## 新增20項精確工程公式','', '|索引|組別|名稱|公式|','|---:|---|---|---|']
    lines += [f"|{x['observationIndex']}|{x['group']}|{x['name']}|{x['formula']}|" for x in formulas]
    (out/'feature-inventory.md').write_text('\n'.join(lines),encoding='utf-8')
