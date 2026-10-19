"""Evaluate frozen checkpoints with explicit action latency (default 1 ms)."""
import argparse
import json
from pathlib import Path
from train_rl_direction import evaluate, MaskablePPO, DummyVecEnv, VecNormalize, NikkeiRlEnv
from direction_warmstart import restored_environment
from feedback import verify_manifest


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--bridge', required=True)
    parser.add_argument('--dataset', required=True)
    parser.add_argument('--run', required=True)
    parser.add_argument('--resume', action='store_true')
    parser.add_argument('--latency-ms', type=int, default=1)
    args = parser.parse_args()
    if not 0 <= args.latency_ms <= 5000:
        raise ValueError('latency-ms must be between 0 and 5000')
    output = Path(args.run)
    report = json.loads((output / 'backtest-report.json').read_text(encoding='utf-8'))
    result_path = output / 'measured-latency-backtest.json'
    results = json.loads(result_path.read_text()) if args.resume and result_path.exists() else []
    results = [row for row in results if row['latencyMs'] == args.latency_ms]
    for training in report['training']:
        seed = training['seed']
        if any(row['seed'] == seed for row in results):
            continue
        model_path = output / f'policy-seed{seed}.onnx'
        manifest = json.loads(model_path.with_suffix('.manifest.json').read_text())
        verify_manifest(model_path, manifest)
        args.actor_excluded_features = manifest.get('actorExcludedFeatures', [])
        args.reward_cost_weight = manifest.get('reward', {}).get('costWeight', 1)
        args.environment_config = restored_environment(manifest)
        timing = json.loads((output / f'latency-seed{seed}.json').read_text())
        latency = args.latency_ms
        if not 0 <= latency <= 5000:
            raise ValueError('latency-ms must be between 0 and 5000')
        vec = DummyVecEnv([lambda: NikkeiRlEnv(args.bridge, args.dataset, report['train'],args.environment_config)])
        norm = VecNormalize.load(output / f'normalizer-seed{seed}.pkl', vec)
        norm.training = False
        norm.norm_reward = False
        try:
            model = MaskablePPO.load(output / f'ppo-seed{seed}.zip', device='cpu')
            row, _ = evaluate(model, norm, args, report['validation'], seed, latency, 0, output)
            row['budgetBasis'] = 'Explicit fixed action latency; user default 1 ms'
            row['measuredActorP99Ms'] = timing['p99Ms']
            results.append(row)
            print(json.dumps(row['summary']), flush=True)
            (output / 'measured-latency-backtest.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
        finally:
            norm.close()


if __name__ == '__main__':
    main()
