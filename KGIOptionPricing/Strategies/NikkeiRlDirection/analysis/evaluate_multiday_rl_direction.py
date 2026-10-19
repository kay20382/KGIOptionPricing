"""Add post-training dates with frozen policies and fixed 1 ms latency."""
import argparse
import hashlib
import json
from pathlib import Path
from train_rl_direction import evaluate, MaskablePPO, DummyVecEnv, VecNormalize, NikkeiRlEnv
from feedback import validate_splits, verify_manifest
from direction_warmstart import restored_environment


parser = argparse.ArgumentParser()
parser.add_argument('--bridge', required=True)
parser.add_argument('--dataset', required=True)
parser.add_argument('--models', required=True)
parser.add_argument('--episodes', nargs='+', required=True)
parser.add_argument('--output', required=True)
parser.add_argument('--latency-ms', type=int, default=1)
args = parser.parse_args()
if not 0 <= args.latency_ms <= 5000:
    raise ValueError('latency-ms must be between 0 and 5000')
models, output = Path(args.models), Path(args.output)
output.mkdir(parents=True, exist_ok=False)
original = json.loads((models / 'backtest-report.json').read_text())
validate_splits(original['train'], args.episodes)
report = dict(trainingEpisodes=original['train'], addedEpisodes=args.episodes, latencyMs=args.latency_ms,
              extraCostTicksPerSide=0, modelsFrozen=True, deploymentEligible=False,
              usage='DevelopmentPreviouslyInspected', scenarios=[], fingerprints={})
for training in original['training']:
    seed = training['seed']
    model_path = models / f'policy-seed{seed}.onnx'
    manifest = json.loads(model_path.with_suffix('.manifest.json').read_text())
    args.actor_excluded_features = manifest.get('actorExcludedFeatures', [])
    args.reward_cost_weight = manifest.get('reward', {}).get('costWeight', 1)
    args.environment_config = restored_environment(manifest)
    verify_manifest(model_path, manifest)
    paths = [model_path, models / f'ppo-seed{seed}.zip', models / f'normalizer-seed{seed}.pkl']
    before = {path.name: hashlib.sha256(path.read_bytes()).hexdigest().upper() for path in paths}
    vec = DummyVecEnv([lambda: NikkeiRlEnv(args.bridge, args.dataset, args.episodes,args.environment_config)])
    norm = VecNormalize.load(models / f'normalizer-seed{seed}.pkl', vec)
    norm.training = False
    norm.norm_reward = False
    try:
        policy = MaskablePPO.load(models / f'ppo-seed{seed}.zip', device='cpu')
        result, _ = evaluate(policy, norm, args, args.episodes, seed, args.latency_ms, 0, output)
        after = {path.name: hashlib.sha256(path.read_bytes()).hexdigest().upper() for path in paths}
        if before != after:
            raise ValueError('Frozen model bundle changed during evaluation')
        report['fingerprints'][str(seed)] = before
        report['scenarios'].append(result)
        print(json.dumps(dict(seed=seed, **result['summary'])), flush=True)
        (output / 'backtest-multiday.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    finally:
        norm.close()
