"""Training-only numerical conditioning; shared JPY ledger and actor inputs unchanged."""
import math
import torch

def configure_reward_normalization(normalizer,policy,enabled,gamma,parent_normalized=False):
    variance=float(normalizer.ret_rms.var)
    if not math.isfinite(variance) or variance<0 or not 0<gamma<1:raise ValueError('Invalid training reward conditioning')
    scale=math.sqrt(variance+normalizer.epsilon)
    changed=bool(enabled)!=bool(parent_normalized)
    factor=(1/scale if enabled else scale) if changed else 1
    if changed:
        # ONNX exports the actor only. Bring inherited value estimates into target units.
        with torch.no_grad():
            policy.value_net.weight.mul_(factor);policy.value_net.bias.mul_(factor)
        for parameter in [*policy.value_net.parameters(),*policy.mlp_extractor.value_net.parameters()]:policy.optimizer.state.pop(parameter,None)
    normalizer.norm_reward=bool(enabled)
    if enabled or parent_normalized:normalizer.gamma=gamma
    return dict(enabled=bool(enabled),parentNormalized=bool(parent_normalized),parentReturnStd=scale,
      criticRescaled=changed,criticScaleFactor=factor,criticOptimizerReset=changed,
      normalizationGamma=normalizer.gamma,clipReward=normalizer.clip_reward,observationStatisticsUnchanged=True,
      scope='Training reward and critic only; authoritative execution, costs, raw JPY performance and ONNX actor inputs unchanged')
