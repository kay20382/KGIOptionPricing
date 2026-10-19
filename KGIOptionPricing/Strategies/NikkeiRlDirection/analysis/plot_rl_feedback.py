"""Plot authoritative replay equity from recorded feedback, not reward."""
import argparse
import gzip
import json
from datetime import datetime
from pathlib import Path
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import matplotlib.dates as mdates

parser = argparse.ArgumentParser()
parser.add_argument('--run', required=True)
args = parser.parse_args()
run = Path(args.run)
plt.rcParams['font.family'] = 'Microsoft JhengHei'
plt.rcParams['axes.unicode_minus'] = False
figure, axes = plt.subplots(figsize=(11, 6), layout='constrained')
for seed in [11, 22, 33, 44, 55]:
    times, equity = [], []
    with gzip.open(run / f'feedback-seed{seed}-delay0-cost0.jsonl.gz', 'rt', encoding='utf-8') as source:
        for line in source:
            state = json.loads(line)['next']
            times.append(datetime.fromisoformat(state['at']).replace(tzinfo=None))
            equity.append(state['netPnl'])
    axes.plot(times, equity, label=f'Seed {seed}', linewidth=1.2)
axes.axhline(0, color='#333333', linestyle='--', linewidth=1, label='等待不交易')
axes.set_title('225MC 強化學習：9/29 開發驗證淨權益\n0 ms 決策延遲、每邊佣金 36 JPY、無額外成本')
axes.set_ylabel('累積淨損益（JPY）')
axes.set_xlabel('台北時間')
axes.xaxis.set_major_formatter(mdates.DateFormatter('%H:%M'))
axes.grid(alpha=.2)
axes.legend(ncol=3, loc='lower left')
figure.savefig(run / 'equity-development.png', dpi=150)
figure.savefig(run / 'equity-development.svg')
plt.close(figure)
