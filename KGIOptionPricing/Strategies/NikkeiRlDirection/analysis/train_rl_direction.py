"""Reproducible PPO backtest, frozen ONNX actor and timing/cost scenario evaluation."""
import argparse
import hashlib
import json
import os
import platform
import time
from pathlib import Path

os.environ.setdefault('OMP_NUM_THREADS','1')
os.environ.setdefault('OPENBLAS_NUM_THREADS','1')
print(f'Loading RL runtime PID={os.getpid()}',flush=True)
import numpy as np
import torch
from sb3_contrib import MaskablePPO
from stable_baselines3.common.vec_env import DummyVecEnv, VecNormalize
from rl_env import NikkeiRlEnv
from feedback import ACTION_NAMES, validate_splits, build_summary
from direction_warmstart import build_direction_samples
from policy_observation import policy_observation
from observation_schema import feature_names,POWER_ACTIONS

torch.set_num_threads(1)

class ActorExport(torch.nn.Module):
    def __init__(self, policy, mean, var, excluded_features=()):
        super().__init__();self.policy=policy
        self.register_buffer('mean',torch.tensor(mean,dtype=torch.float32))
        self.register_buffer('var',torch.tensor(var,dtype=torch.float32))
        mask=torch.ones(len(mean),dtype=torch.float32)
        mask[list(excluded_features)]=0
        self.register_buffer('feature_mask',mask)
    def forward(self, observation):
        normalized=torch.clamp((observation*self.feature_mask-self.mean)/torch.sqrt(self.var+1e-8),-10,10)
        features=self.policy.extract_features(normalized)
        latent=self.policy.mlp_extractor.forward_actor(features)
        return self.policy.action_net(latent)

def evaluate(model, normalizer, args, episodes, seed, latency, cost, output):
    path=output/f'feedback-seed{seed}-delay{latency}-cost{cost}.jsonl.gz'
    env=NikkeiRlEnv(args.bridge,args.dataset,episodes,dict(getattr(args,'environment_config',{}),decisionLatencyMs=latency,extraCostTicksPerSide=cost,
                   rewardCostWeight=getattr(args,'reward_cost_weight',1)),path if getattr(args,'retain_feedback',False) else None,
                   excluded_actor_features=getattr(args,'actor_excluded_features',()))
    results=[];parity=[];timings=[]
    try:
        for episode in episodes:
            obs,_=env.reset(seed=seed,options={'episode':episode})
            while True:
                mask=env.action_masks();start=time.perf_counter_ns()
                normalized=normalizer.normalize_obs(obs[np.newaxis,:])
                action,_=model.predict(normalized,action_masks=mask,deterministic=True)
                timings.append((time.perf_counter_ns()-start)/1e6)
                if len(parity)<32:
                    with torch.no_grad():
                        logits=model.policy.action_net(model.policy.mlp_extractor.forward_actor(model.policy.extract_features(torch.tensor(normalized)))).numpy()[0]
                    masked=np.where(mask,logits,-1e30);p=np.exp(masked-masked.max());p=p/p.sum()
                    parity.append(dict(observation=env.last['observation'],mask=mask.tolist(),probabilities=p.tolist(),action=int(action[0])))
                obs,reward,terminated,truncated,info=env.step(action[0])
                if terminated or truncated:
                    results.append(dict(episode=episode,**{k:info[k] for k in ['status','netPnl','costs','completedTrades','maxDrawdown','totalReward','winRate','averageWinningPoints','averageLosingPoints','userDefinedProfitFactor','position']}));break
    finally: env.close()
    return dict(seed=seed,latencyMs=latency,extraCostTicksPerSide=cost,episodes=results,
                pythonInferenceMs={f'p{n}':float(np.percentile(timings,n)) for n in [50,95,99]},
                summary=build_summary(results,'DevelopmentPreviouslyInspected')),parity

def collect_direction_data(args, output):
    env=NikkeiRlEnv(args.bridge,args.dataset,args.train,args.environment_config,training=True)
    xs,ys,details=[],[],[]
    try:
        for episode in args.train:
            env.reset(options={'episode':episode}); states=[env.last]
            while True:
                _,_,terminated,truncated,info=env.step(0); states.append(info)
                if terminated or truncated: break
            x,y=build_direction_samples(states,observation_size=args.observation_size,horizon_seconds=args.prediction_horizon_seconds,decision_interval_seconds=args.decision_interval_seconds)
            xs.append(x);ys.append(y)
            details.append(dict(episode=episode,samples=len(y),classCounts=np.bincount(y,minlength=3).tolist()))
    finally: env.close()
    x,y=np.concatenate(xs),np.concatenate(ys)
    if len(y)<100 or np.any(np.bincount(y,minlength=3)==0): raise ValueError('Insufficient direction classes')
    np.savez_compressed(output/'direction-training.npz',observation=x,action=y)
    dataset=json.loads(Path(args.dataset).read_text())
    evidence=dict(horizonSeconds=args.prediction_horizon_seconds,decisionIntervalSeconds=args.decision_interval_seconds,exclusiveThresholdTicks=2,label='Future last-trade direction proxy, not executable PnL',
        episodes=details,cacheSha256={e:hashlib.sha256(Path(dataset[e]).read_bytes()).hexdigest().upper() for e in args.train},
        npzSha256=hashlib.sha256((output/'direction-training.npz').read_bytes()).hexdigest().upper())
    (output/'direction-training.json').write_text(json.dumps(evidence,indent=2),encoding='utf-8')
    return x,y

def pretrain_actor(model, norm, samples, excluded_features, epochs):
    x,y=samples
    x=np.stack([policy_observation(row,excluded_features) for row in x])
    norm.obs_rms.update(x)
    inputs=torch.tensor(norm.normalize_obs(x),dtype=torch.float32);targets=torch.tensor(y)
    counts=np.bincount(y,minlength=3)
    weights=torch.ones(5);weights[:3]=torch.tensor(len(y)/(3*counts),dtype=torch.float32)
    parameters=list(model.policy.mlp_extractor.policy_net.parameters())+list(model.policy.action_net.parameters())
    optimizer=torch.optim.Adam(parameters,lr=0.001)
    model.policy.set_training_mode(True);losses=[]
    for _ in range(epochs):
        total=0
        for batch in torch.randperm(len(y)).split(256):
            logits=model.policy.action_net(model.policy.mlp_extractor.forward_actor(model.policy.extract_features(inputs[batch])))
            loss=torch.nn.functional.cross_entropy(logits,targets[batch],weight=weights)
            optimizer.zero_grad();loss.backward();torch.nn.utils.clip_grad_norm_(parameters,0.5);optimizer.step()
            total+=loss.item()*len(batch)
        losses.append(total/len(y))
    return dict(epochs=epochs,samples=len(y),classCounts=counts.tolist(),weightedTrainingLoss=losses,
                scope='Training-only supervised warmstart; PPO subsequently learns executions and exits')

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--bridge',required=True);parser.add_argument('--dataset',required=True)
    parser.add_argument('--train',nargs='+',required=True);parser.add_argument('--validation',nargs='+',required=True)
    parser.add_argument('--output',required=True);parser.add_argument('--steps',type=int,default=20000)
    parser.add_argument('--seeds',nargs='+',type=int,default=[11,22,33,44,55])
    parser.add_argument('--latencies',nargs='+',type=int,default=[0,100,250])
    parser.add_argument('--costs',nargs='+',type=int,default=[0,1,2])
    parser.add_argument('--training-latency-ms',type=int,default=1)
    parser.add_argument('--entry-price-mode',choices=['same-side','opposite-touch'],default='same-side')
    parser.add_argument('--allow-entry-transfer',action='store_true',help='Explicitly retrain same-side parent with opposite-touch limit pricing')
    parser.add_argument('--allow-earlier-training-extension',action='store_true',help='Add only earlier training dates, retaining all original episodes and other contracts')
    parser.add_argument('--research-readiness-profile',choices=['all-research','exclude-flow'],default='all-research')
    parser.add_argument('--allow-readiness-transfer',action='store_true',help='Explicitly transfer the same research parent to active flow-masked readiness')
    parser.add_argument('--decision-interval-seconds',type=int,choices=[3,5],default=3)
    parser.add_argument('--prediction-horizon-seconds',type=int,choices=[15,30],default=15)
    parser.add_argument('--feature-profile',choices=['three-product','mini-micro-power','mini-micro-research'],default='mini-micro-power')
    parser.add_argument('--risk-config',help='Explicit training stop configuration, merged and validated by C#')
    parser.add_argument('--actor-excluded-features',nargs='*',type=int,default=[])
    parser.add_argument('--reward-cost-weight',type=float,default=1)
    parser.add_argument('--direction-warmstart-epochs',type=int,default=0)
    parser.add_argument('--cooldown-seconds',type=int,default=10)
    parser.add_argument('--observation-size',type=int,choices=[40,48,50,56,60],default=40)
    parser.add_argument('--warmstart-labels')
    parser.add_argument('--warmstart-target',choices=['proxy','executable'],default='proxy')
    parser.add_argument('--warmstart-all-executable',action='store_true')
    parser.add_argument('--resume-parent',help='Parent policy-seedN.onnx; matching PPO, normalizer and manifest required')
    parser.add_argument('--learning-rate',type=float,default=0.0003)
    parser.add_argument('--allow-factor-transfer',action='store_true',help='Explicitly mask additional parent features; reset only their normalization statistics')
    parser.add_argument('--reward-objective',choices=['ledger','constrained-gross'],default='ledger')
    parser.add_argument('--trade-incentive',type=float,default=1)
    parser.add_argument('--objective-win-weight',type=float,default=6)
    parser.add_argument('--objective-gross-weight',type=float,default=2)
    parser.add_argument('--normalize-training-reward',action='store_true')
    parser.add_argument('--dual-feedback-rates',nargs=3,type=float,help='Training episode multiplier update rates: frequency, win, gross')
    parser.add_argument('--greedy-dual-assessment',action='store_true',help='Use deterministic training-date replay rather than sampled rollout counts for feedback')
    parser.add_argument('--cycle-training-episodes',action='store_true',help='Deterministic round-robin training episodes')
    parser.add_argument('--freeze-normalizer',action='store_true')
    parser.add_argument('--execution-factor-head',help='Train-only frozen execution-factor head .pt; raw C# input remains60')
    parser.add_argument('--interaction-factor-head',help='Frozen shallow-tree probabilities .pt; raw C# input remains60')
    parser.add_argument('--retain-feedback',action='store_true',help='Explicit debug opt-in; default retains performance reports only')
    parser.add_argument('--allow-readiness-training-extension',action='store_true',help='Exclusive explicit all-research to exclude-flow transfer plus earlier training dates; all other parent contracts preserved')
    args=parser.parse_args();validate_splits(args.train,args.validation)
    if args.allow_entry_transfer and (not args.resume_parent or args.entry_price_mode!='opposite-touch'):raise ValueError('Entry transfer requires a parent and opposite-touch target')
    if args.allow_earlier_training_extension and not args.resume_parent:raise ValueError('Earlier training extension requires a parent')
    if args.allow_readiness_transfer and (not args.resume_parent or args.research_readiness_profile!='exclude-flow'):raise ValueError('Readiness transfer requires parent and exclude-flow')
    if args.allow_readiness_training_extension and (not args.resume_parent or args.research_readiness_profile!='exclude-flow'):raise ValueError('Readiness/earlier transfer requires parent and exclude-flow')
    if args.research_readiness_profile=='exclude-flow' and (args.feature_profile!='mini-micro-research' or args.observation_size!=60 or args.decision_interval_seconds!=3 or len(set(args.actor_excluded_features))!=len(args.actor_excluded_features) or not set(range(48,54)).issubset(args.actor_excluded_features)):raise ValueError('Exclude-flow requires research60 at3s and all six flow inputs masked')
    from training_objective import objective_config
    shaping=objective_config(args.reward_objective,args.trade_incentive,args.objective_win_weight,args.objective_gross_weight)
    if args.greedy_dual_assessment and not args.dual_feedback_rates:raise ValueError('Greedy assessment requires explicit dual feedback rates')
    if args.dual_feedback_rates:
        if shaping is None:raise ValueError('Dual feedback requires constrained-gross objective')
        from dual_feedback import DualConstraintFeedback
        DualConstraintFeedback(shaping,rates=args.dual_feedback_rates)
    if args.steps<1: raise ValueError('steps must be positive')
    if args.execution_factor_head and (not args.resume_parent or not args.freeze_normalizer or args.observation_size!=60):raise ValueError('Execution factors require60-input parent and frozen normalization')
    if args.interaction_factor_head and (args.execution_factor_head or not args.resume_parent or not args.freeze_normalizer or args.observation_size!=60):raise ValueError('Interaction factors require exclusive60-input parent and frozen normalization')
    if args.direction_warmstart_epochs<0 or args.cooldown_seconds<0: raise ValueError('Invalid training parameters')
    power=args.feature_profile in ['mini-micro-power','mini-micro-research']
    args.environment_config=dict(cooldownSeconds=args.cooldown_seconds,decisionLatencyMs=args.training_latency_ms,
        rewardCostWeight=args.reward_cost_weight,observationSize=args.observation_size,decisionIntervalSeconds=args.decision_interval_seconds,featureProfile=args.feature_profile,directionDrivenHolding=power,maxHoldingSeconds=0 if power else 60)
    if args.entry_price_mode!='same-side':args.environment_config['entryPriceMode']=args.entry_price_mode
    if args.research_readiness_profile!='all-research':args.environment_config['researchReadinessProfile']=args.research_readiness_profile
    if args.risk_config:
        risk=json.loads(Path(args.risk_config).read_text(encoding='utf-8-sig'))
        if set(risk)-{'stopMode','fixedStopPoints','minStopPoints','maxStopPoints','stopVolatilitySeconds','stopVolatilityMultiplier'}:raise ValueError('Risk config cannot alter feature/execution contract')
        if args.feature_profile=='mini-micro-research' and (risk.get('stopMode')!='fixed' or risk.get('fixedStopPoints')!=150 or risk.get('maxStopPoints')!=150):raise ValueError('Research experiment requires fixed150points')
        args.environment_config.update(risk)
    elif args.feature_profile=='mini-micro-research':raise ValueError('Research training requires explicit150point risk config')
    names=feature_names(args.observation_size,args.feature_profile)
    parent_provenance=None
    if args.learning_rate<=0:raise ValueError('Learning rate must be positive')
    if args.resume_parent:
        if args.direction_warmstart_epochs or args.warmstart_labels:raise ValueError('Resume cannot repeat supervised warmstart')
        from resume_contract import validate_parent
        parent_path=Path(args.resume_parent)
        parent_manifest=json.loads(parent_path.with_suffix('.manifest.json').read_text(encoding='utf-8'))
        validate_parent(parent_manifest,args.observation_size,names,args.actor_excluded_features,args.train,args.environment_config,args.prediction_horizon_seconds,args.allow_factor_transfer,args.allow_entry_transfer,args.allow_earlier_training_extension,args.allow_readiness_transfer,args.allow_readiness_training_extension)
        parent_seed=parent_manifest['seed']
        parent_ppo=parent_path.parent/f'ppo-seed{parent_seed}.zip'
        parent_norm=parent_path.parent/f'normalizer-seed{parent_seed}.pkl'
        paths=[parent_path,parent_path.with_suffix('.manifest.json'),parent_ppo,parent_norm]
        parent_provenance=dict(files={str(p.resolve()):hashlib.sha256(p.read_bytes()).hexdigest().upper() for p in paths},seed=parent_seed,
                               additionalRequestedSteps=args.steps,learningRate=args.learning_rate,repeatWarmstart=False,
                               factorTransfer=args.allow_factor_transfer,parentExcludedFeatures=parent_manifest['actorExcludedFeatures'],targetExcludedFeatures=args.actor_excluded_features)
        parent_provenance['entryTransfer']=args.allow_entry_transfer
        parent_provenance['earlierTrainingExtension']=args.allow_earlier_training_extension or args.allow_readiness_training_extension
        parent_provenance['readinessTransfer']=args.allow_readiness_transfer or args.allow_readiness_training_extension
        parent_provenance['readinessTrainingExtension']=args.allow_readiness_training_extension
        if args.allow_readiness_transfer or args.allow_readiness_training_extension:
            parent_provenance['parentResearchReadinessProfile']=parent_manifest['environmentConfig'].get('researchReadinessProfile','all-research')
            parent_provenance['targetResearchReadinessProfile']=args.research_readiness_profile
            parent_provenance['trainingDatasetSha256']=hashlib.sha256(Path(args.dataset).read_bytes()).hexdigest().upper()
        if args.allow_earlier_training_extension or args.allow_readiness_training_extension:
            parent_provenance['originalTrainingEpisodes']=parent_manifest['trainingEpisodes']
            parent_provenance['addedEarlierTrainingEpisodes']=sorted(set(args.train)-set(parent_manifest['trainingEpisodes']))
            parent_provenance['trainingDatasetSha256']=hashlib.sha256(Path(args.dataset).read_bytes()).hexdigest().upper()
        if parent_provenance['files'][str(parent_path.resolve())]!=parent_manifest['modelSha256']:raise ValueError('Parent ONNX identity mismatch')
    actions=POWER_ACTIONS if power else ACTION_NAMES
    if args.prediction_horizon_seconds%args.decision_interval_seconds:raise ValueError('Prediction horizon must align with decision cadence')
    if any(i<0 or i>=args.observation_size for i in args.actor_excluded_features): raise ValueError('Invalid excluded features')
    output=Path(args.output);output.mkdir(parents=True,exist_ok=False)
    import importlib.metadata as metadata
    versions={p:metadata.version(p) for p in ['torch','gymnasium','stable-baselines3','sb3-contrib','numpy','onnx']}
    report=dict(status='IncompleteDevelopmentValidation',deploymentEligible=False,train=args.train,validation=args.validation,
                requestedSteps=args.steps,versions=versions,python=platform.python_version(),
                trainingActionLatencyMs=args.training_latency_ms,
                actorExcludedFeatures=args.actor_excluded_features,rewardCostWeight=args.reward_cost_weight,
                environmentConfig=args.environment_config,featureNames=names,
                predictionHorizonSeconds=args.prediction_horizon_seconds,directionWarmstartEpochs=args.direction_warmstart_epochs,scenarios=[])
    direction_samples=None;warmstart_provenance=None
    if args.warmstart_labels:
        if args.observation_size not in [40,60] or not args.direction_warmstart_epochs:raise ValueError('Executable labels require40/60inputs and warmstart epochs')
        from executable_warmstart import load_samples
        direction_samples,warmstart_provenance=load_samples(args.warmstart_labels,args.dataset,args.train,args.warmstart_target,output,matched_only=not args.warmstart_all_executable,training_config=args.environment_config,horizon_seconds=args.prediction_horizon_seconds)
        report['warmstartProvenance']=warmstart_provenance
    elif args.direction_warmstart_epochs:
        direction_samples=collect_direction_data(args,output)
    for seed in args.seeds:
        print(f'TRAIN seed={seed} steps={args.steps}',flush=True)
        dual=DualConstraintFeedback(shaping,rates=args.dual_feedback_rates) if args.dual_feedback_rates else None
        vec=DummyVecEnv([lambda:NikkeiRlEnv(args.bridge,args.dataset,args.train,
                        config=args.environment_config,training=True,
                        excluded_actor_features=args.actor_excluded_features,
                        training_objective=shaping,
                        objective_trace=None,dual_feedback=dual,cycle_training_episodes=args.cycle_training_episodes)])
        norm=VecNormalize.load(str(parent_norm),vec) if args.resume_parent else VecNormalize(vec,norm_obs=True,norm_reward=False,clip_obs=10)
        assessment_env=None
        norm.training=not args.freeze_normalizer;norm.norm_reward=False
        if args.resume_parent and args.allow_factor_transfer:
            from resume_contract import transfer_statistics
            mean,var=transfer_statistics(norm.obs_rms.mean,norm.obs_rms.var,parent_manifest['actorExcludedFeatures'],args.actor_excluded_features)
            norm.obs_rms.mean=np.asarray(mean);norm.obs_rms.var=np.asarray(var)
        try:
            policy_kwargs=dict(net_arch=dict(pi=[64,64],vf=[64,64]))
            if args.execution_factor_head:
                from execution_factors import ExecutionFactorExtractor,transplant_policy,DERIVED_NAMES
                policy_kwargs['features_extractor_class']=ExecutionFactorExtractor
                head_path=Path(args.execution_factor_head);head_meta=json.loads(head_path.with_suffix('.json').read_text(encoding='utf-8'))
                if head_meta['trainingEpisodes']!=args.train or head_meta['actorExcludedFeatures']!=args.actor_excluded_features or head_meta['parentNormalizerSha256']!=hashlib.sha256(parent_norm.read_bytes()).hexdigest().upper() or head_meta['headSha256']!=hashlib.sha256(head_path.read_bytes()).hexdigest().upper():raise ValueError('Execution factor provenance mismatch')
            if args.interaction_factor_head:
                from interaction_head import InteractionFactorExtractor,DERIVED_NAMES
                head_path=Path(args.interaction_factor_head);head_meta=json.loads(head_path.with_suffix('.json').read_text(encoding='utf-8'))
                if head_meta['trainingEpisodes']!=args.train or head_meta['actorExcludedFeatures']!=args.actor_excluded_features or head_meta['parentNormalizerSha256']!=hashlib.sha256(parent_norm.read_bytes()).hexdigest().upper() or head_meta['headSha256']!=hashlib.sha256(head_path.read_bytes()).hexdigest().upper():raise ValueError('Interaction factor provenance mismatch')
                policy_kwargs['features_extractor_class']=InteractionFactorExtractor
                policy_kwargs['features_extractor_kwargs']=dict(head_spec=head_meta['headSpec'])
            model=MaskablePPO('MlpPolicy',norm,learning_rate=args.learning_rate,n_steps=2048,batch_size=64,n_epochs=10,
                gamma=0.99**(args.decision_interval_seconds/5),gae_lambda=0.95**(args.decision_interval_seconds/5),clip_range=0.2,ent_coef=0.01,target_kl=0.02,
                policy_kwargs=policy_kwargs,seed=seed,device='cpu',verbose=0)
            if args.resume_parent:
                parent_model=MaskablePPO.load(str(parent_ppo),env=norm,learning_rate=args.learning_rate,device='cpu')
                if args.execution_factor_head or args.interaction_factor_head:
                    from execution_factors import transplant_policy
                    transplant_policy(parent_model.policy,model.policy)
                    model.policy.features_extractor.head.load_state_dict(torch.load(head_path,weights_only=True,map_location='cpu'))
                    model.num_timesteps=parent_model.num_timesteps
                else:model=parent_model
                model.set_random_seed(seed)
            from reward_conditioning import configure_reward_normalization
            reward_conditioning=configure_reward_normalization(norm,model.policy,args.normalize_training_reward,model.gamma,
                parent_normalized=bool(parent_manifest.get('trainingRewardNormalization',{}).get('enabled',False)) if args.resume_parent else False)
            initial_steps=model.num_timesteps
            if args.greedy_dual_assessment:
                from greedy_assessment import assess_training_episode
                assessment_env=NikkeiRlEnv(args.bridge,args.dataset,args.train,args.environment_config,excluded_actor_features=args.actor_excluded_features)
                vec.envs[0].dual_assessment=lambda episode:assess_training_episode(model,norm,assessment_env,episode,args.train)
            if direction_samples is not None:
                report.setdefault('warmstart',[]).append(dict(seed=seed,**pretrain_actor(model,norm,direction_samples,
                    args.actor_excluded_features,args.direction_warmstart_epochs)))
            start=time.perf_counter();model.learn(total_timesteps=args.steps,reset_num_timesteps=not bool(args.resume_parent))
            report.setdefault('training',[]).append(dict(seed=seed,actualSteps=model.num_timesteps,initialSteps=initial_steps,additionalSteps=model.num_timesteps-initial_steps,seconds=time.perf_counter()-start))
            if parent_provenance is not None:report['resumeProvenance']=parent_provenance
            report['trainingRewardNormalization']=reward_conditioning
            report.setdefault('trainingDiagnostics',[]).append(dict(seed=seed,metrics={k:float(v) for k,v in model.logger.name_to_value.items() if k.startswith('train/') and np.isscalar(v) and np.isfinite(v)}))
            report['trainingObjective']=dict(mode=args.reward_objective,tradeIncentive=args.trade_incentive,frequencyCap=50,winWeight=args.objective_win_weight,grossWeight=args.objective_gross_weight,
                accounting='Unchanged authoritative C# ledger; shaping affects training only',scope='Fixed Lagrangian proxy; does not certify hard constraints')
            report['trainingObjective']['episodeOrder']='Cyclic' if args.cycle_training_episodes else 'Random'
            if dual is not None:
                report.setdefault('dualFeedback',[]).append(dict(seed=seed,initialWeights=shaping,rates=dual.rates,caps=dual.caps,
                    finalWeights=dual.config(),completedTrainingEpisodes=dual.history,assessmentMode='GreedyReplay' if args.greedy_dual_assessment else 'SampledRollout',scope='Training only; actor and validation have no adaptive multiplier path'))
                report['trainingObjective']['scope']='Training-only projected multiplier feedback; does not certify hard constraints'
            model.save(output/f'ppo-seed{seed}');norm.save(output/f'normalizer-seed{seed}.pkl');norm.training=False
            model.policy.set_training_mode(False)
            actor=ActorExport(model.policy,norm.obs_rms.mean,norm.obs_rms.var,args.actor_excluded_features).eval()
            modelpath=output/f'policy-seed{seed}.onnx'
            torch.onnx.export(actor,(torch.zeros(1,args.observation_size),),str(modelpath),input_names=['observation'],output_names=['logits'],
                dynamic_axes={'observation':{0:'batch'},'logits':{0:'batch'}},opset_version=17,dynamo=False)
            manifest=dict(schema='nikkei-rl-research-pruned-flow-v1' if args.research_readiness_profile=='exclude-flow' else 'nikkei-rl-research-v1' if args.feature_profile=='mini-micro-research' else 'nikkei-rl-power-v1' if power else {40:'nikkei-rl-v1',48:'nikkei-rl-v3',50:'nikkei-rl-v2',56:'nikkei-rl-v4'}[args.observation_size],modelSha256=hashlib.sha256(modelpath.read_bytes()).hexdigest().upper(),
                observationSize=args.observation_size,featureNames=names,actionNames=actions,trainingEpisodes=args.train,validationEpisodes=args.validation,
                seed=seed,versions=versions,deterministic=True,reward=dict(drawdownPenalty=0.25,costWeight=args.reward_cost_weight),
                costs=dict(commissionPerSide=36,tickSize=5,multiplier=10),normalizationEmbedded=True,
                trainingActionLatencyMs=args.training_latency_ms,
                actorExcludedFeatures=args.actor_excluded_features,
                environmentConfig=args.environment_config,predictionHorizonSeconds=args.prediction_horizon_seconds,discount=dict(gamma=0.99**(args.decision_interval_seconds/5),gaeLambda=0.95**(args.decision_interval_seconds/5),referenceIntervalSeconds=5),directionWarmstartEpochs=args.direction_warmstart_epochs,
                usage='DevelopmentPreviouslyInspected',deploymentEligible=False)
            if warmstart_provenance is not None:manifest['warmstartProvenance']=warmstart_provenance
            if parent_provenance is not None:manifest['resumeProvenance']=dict(parent_provenance,initialSteps=initial_steps,totalSteps=model.num_timesteps,additionalSteps=model.num_timesteps-initial_steps)
            manifest['trainingObjective']=report['trainingObjective']
            if dual is not None:manifest['dualFeedback']=dict(rates=dual.rates,caps=dual.caps,completedTrainingEpisodes=len(dual.history),finalWeights=dual.config(),inferenceAdaptive=False,assessmentMode='GreedyReplay' if args.greedy_dual_assessment else 'SampledRollout')
            manifest['trainingRewardNormalization']=reward_conditioning
            manifest['normalizationFrozenDuringContinuation']=args.freeze_normalizer
            if args.execution_factor_head:
                manifest['derivedExecutionFactors']=dict(names=DERIVED_NAMES,provenance=head_meta,frozenDuringPpo=True,rawInputSize=60,policyFeatureSize=68,optimizerResetForExpandedPolicy=True)
            if args.interaction_factor_head:
                manifest['derivedInteractionFactors']=dict(names=DERIVED_NAMES,provenance=head_meta,frozenDuringPpo=True,rawInputSize=60,policyFeatureSize=63,optimizerResetForExpandedPolicy=True)
            modelpath.with_suffix('.manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
            for latency in args.latencies:
                for cost in args.costs:
                    result,parity=evaluate(model,norm,args,args.validation,seed,latency,cost,output)
                    report['scenarios'].append(result)
                    print(json.dumps(dict(seed=seed,delay=latency,cost=cost,**result['summary'])),flush=True)
                    if latency==args.latencies[0] and cost==args.costs[0]:
                        (output/f'parity-seed{seed}.json').write_text(json.dumps(parity,separators=(',',':')),encoding='utf-8')
                    (output/'backtest-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
        finally:
            norm.close()
            if assessment_env is not None:assessment_env.close()
    lines=['# 日經微型強化學習回測','', '狀態：開發驗證，資料已於先前研究使用；不可宣稱全新樣本外或實盤合格。','',
           '| Seed | 決策／送單延遲 ms | 每邊額外成本跳數 | 完整交易 | 淨損益 JPY | 最大盤別回撤 JPY | 狀態 |',
           '|---|---:|---:|---:|---:|---:|---|']
    for row in report['scenarios']:
        s=row['summary'];lines.append(f"| {row['seed']} | {row['latencyMs']} | {row['extraCostTicksPerSide']} | {s['completedTrades']} | {s['netPnl']:.2f} | {s['maximumEpisodeDrawdown']:.2f} | {s['status']} |")
    lines+=['','成本跳數為額外成交成本敏感度，不是價格滑價模型；延遲則實際移動委託可送出時間，使用之後行情判斷成交。',
            '使用共用 paper-strict-cross 成交／帳本；未平倉 episode 維持 Incomplete，不虛構最後成交。',
            '訓練與推論套件版本及每個模型 hash 另存 manifest；ONNX 已包含訓練資料擬合的 normalization。']
    (output/'backtest-report.md').write_text('\n'.join(lines),encoding='utf-8')

if __name__=='__main__': main()
