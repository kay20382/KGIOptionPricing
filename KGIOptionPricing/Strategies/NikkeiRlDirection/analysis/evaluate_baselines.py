"""Fixed causal baselines using the same C# execution and accounting engine."""
import argparse
import json
from pathlib import Path
from rl_env import NikkeiRlEnv
from feedback import build_summary


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--bridge', required=True)
    parser.add_argument('--dataset', required=True)
    parser.add_argument('--episodes', nargs='+', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    rows = []
    for name in ['Wait', 'Aligned60SecondTrend']:
        for latency in [0, 100, 250]:
            for cost in [0, 1, 2]:
                env = NikkeiRlEnv(args.bridge, args.dataset, args.episodes,
                                  dict(decisionLatencyMs=latency, extraCostTicksPerSide=cost),
                                  output / f'baseline-{name}-delay{latency}-cost{cost}.jsonl.gz')
                episodes = []
                try:
                    for episode in args.episodes:
                        observation, _ = env.reset(options={'episode': episode})
                        while True:
                            mask = env.action_masks()
                            action = 0
                            if name == 'Aligned60SecondTrend':
                                returns = observation[[3, 13, 23]]
                                direction = 1 if all(returns > 0) else -1 if all(returns < 0) else 0
                                if observation[30] and direction != observation[30] and mask[3]:
                                    action = 3
                                elif direction == 1 and mask[1]:
                                    action = 1
                                elif direction == -1 and mask[2]:
                                    action = 2
                            observation, _, terminated, truncated, info = env.step(action)
                            if terminated or truncated:
                                episodes.append(dict(episode=episode, **{key: info[key] for key in
                                    ['status', 'netPnl', 'costs', 'completedTrades', 'maxDrawdown', 'position']}))
                                break
                finally:
                    env.close()
                row = dict(baseline=name, latencyMs=latency, extraCostTicksPerSide=cost,
                           episodes=episodes, summary=build_summary(episodes, 'DevelopmentPreviouslyInspected'))
                rows.append(row)
                print(json.dumps(row), flush=True)
                (output / 'baselines.json').write_text(json.dumps(rows, indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()
