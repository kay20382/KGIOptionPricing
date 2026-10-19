"""Gymnasium facade of the existing C# paper engine. No Python execution/P&L clone."""
import json
import gzip
import subprocess
from pathlib import Path
import gymnasium as gym
import numpy as np
from policy_observation import policy_observation

class NikkeiRlEnv(gym.Env):
    metadata={'render_modes': []}

    def __init__(self, bridge, dataset, episodes, config=None, feedback_path=None, training=False, excluded_actor_features=(),training_objective=None,objective_trace=None,dual_feedback=None,cycle_training_episodes=False,dual_assessment=None):
        super().__init__()
        if not episodes: raise ValueError('無可用 episode')
        self.episodes=list(episodes);self.config=config or {};self.step_id=0;self.training=training
        if training and self.config.get('allowIncompleteEvaluation',False):
            raise ValueError('Evaluation-only opt-in cannot be used for training')
        if dual_feedback is not None and (not training or not training_objective):raise ValueError('Dual feedback requires training objective')
        if dual_assessment is not None and dual_feedback is None:raise ValueError('Greedy assessment requires training dual feedback')
        if cycle_training_episodes and not training:raise ValueError('Episode cycling is training-only')
        self.dual_feedback=dual_feedback;self.dual_assessment=dual_assessment;self.cycle_training_episodes=cycle_training_episodes;self.episode_cursor=0
        size=self.config.get('observationSize',40)
        if size not in [40,48,50,56,60]: raise ValueError('Invalid observation size')
        self.observation_space=gym.spaces.Box(-np.inf,np.inf,shape=(size,),dtype=np.float32)
        self.action_space=gym.spaces.Discrete(5)
        self.process=subprocess.Popen(['dotnet',str(Path(bridge).resolve()),'serve',str(Path(dataset).resolve())],
                                      stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,
                                      text=True,encoding='utf-8',bufsize=1)
        self.feedback=gzip.open(feedback_path,'wt',encoding='utf-8',compresslevel=1) if feedback_path else None
        self.last=None;self.episode=None
        self.excluded_actor_features=tuple(excluded_actor_features)
        if training_objective and not training:raise ValueError('Objective shaping is training-only')
        self.training_objective=training_objective
        self.objective_trace=Path(objective_trace).open('x',encoding='utf-8') if objective_trace else None
        self.objective_reward_sum=0.;self.objective_terms={}

    def request(self, payload):
        self.process.stdin.write(json.dumps(payload,separators=(',',':'))+'\n');self.process.stdin.flush()
        row=self.process.stdout.readline()
        if not row: raise RuntimeError('C# 回測程序停止')
        state=json.loads(row)
        if 'error' in state: raise RuntimeError(state['error'])
        return state

    def reset(self, *, seed=None, options=None):
        super().reset(seed=seed)
        if options and 'episode' in options:self.episode=options['episode']
        elif self.cycle_training_episodes:
            self.episode=self.episodes[self.episode_cursor%len(self.episodes)];self.episode_cursor+=1
        else:self.episode=self.episodes[int(self.np_random.integers(len(self.episodes)))]
        if self.dual_feedback is not None:self.training_objective=self.dual_feedback.config()
        self.step_id=0
        self.last=self.request(dict(op='reset',episode=self.episode,config=self.config))
        self.objective_reward_sum=0.;self.objective_terms={}
        return policy_observation(self.last['observation'],self.excluded_actor_features),self.last

    def action_masks(self): return np.asarray(self.last['mask'],dtype=bool)

    def step(self, action):
        before=self.last
        self.last=self.request(dict(op='step',stepId=self.step_id,action=int(action)))
        if self.feedback:
            self.feedback.write(json.dumps(dict(episode=self.episode,stepId=self.step_id,observation=before['observation'],
                mask=before['mask'],proposedAction=int(action),next=self.last),ensure_ascii=False,separators=(',',':'))+'\n')
        self.step_id+=1
        if self.last['status']=='IncompleteDataGap' and not self.config.get('allowIncompleteEvaluation',False): raise RuntimeError('資料缺口不可加入 PPO rollout')
        if self.training and self.last['truncated']: raise RuntimeError('不完整 episode 不可加入 PPO rollout: '+self.last['status'])
        reward=float(self.last['reward'])
        if self.training_objective:
            from training_objective import constrained_reward
            reward,terms=constrained_reward(before,self.last,**self.training_objective)
            self.objective_reward_sum+=reward
            for key,value in terms.items():self.objective_terms[key]=self.objective_terms.get(key,0)+value
            if self.objective_trace and (self.last['terminated'] or self.last['truncated']):
                self.objective_trace.write(json.dumps(dict(episode=self.episode,completedTrades=self.last['completedTrades'],
                    completedGrossPnlJPY=self.last['completedGrossPnl'],completedNetPnlJPY=self.last['completedNetPnl'],winRate=self.last['winRate'],
                    objectiveReward=self.objective_reward_sum,terms=self.objective_terms,status=self.last['status']))+'\n');self.objective_trace.flush()
        if self.dual_feedback is not None and self.last['terminated']:
            assessed=self.dual_assessment(self.episode) if self.dual_assessment is not None else self.last
            record=self.dual_feedback.update(self.episode,assessed)
            if self.dual_assessment is not None:
                record['assessmentMode']='GreedyFrozenPolicyTrainingReplay'
                record['sampledTrainingEpisode']={k:self.last[k] for k in ['completedTrades','completedGrossPnl','completedNetPnl','winRate']}
        return (policy_observation(self.last['observation'],self.excluded_actor_features),reward,
                self.last['terminated'],self.last['truncated'],self.last)

    def close(self):
        if self.process.poll() is None:
            try: self.process.stdin.write('{"op":"close"}\n');self.process.stdin.flush();self.process.wait(timeout=10)
            except (OSError,subprocess.TimeoutExpired): self.process.terminate();self.process.wait(timeout=10)
        if self.feedback: self.feedback.close();self.feedback=None
        if self.objective_trace:self.objective_trace.close();self.objective_trace=None
