"""Training-date deterministic policy assessment through the unchanged C# paper engine."""
def assess_training_episode(model,normalizer,env,episode,training_episodes):
    if episode not in training_episodes or env.training:raise ValueError('Assessment must replay only training dates without adaptive feedback')
    obs,_=env.reset(options={'episode':episode})
    while True:
        action,_=model.predict(normalizer.normalize_obs(obs[None,:]),action_masks=env.action_masks(),deterministic=True)
        obs,_,done,truncated,state=env.step(action[0])
        if truncated:raise ValueError('Incomplete greedy assessment cannot update training multipliers')
        if done:return state
