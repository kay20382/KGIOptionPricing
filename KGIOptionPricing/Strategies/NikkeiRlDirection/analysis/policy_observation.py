import numpy as np


def policy_observation(observation, excluded_features=()):
    value = np.asarray(observation, dtype=np.float32).copy()
    if value.shape not in [(40,),(48,),(50,),(56,),(60,)] or not np.all(np.isfinite(value)):
        raise ValueError('Invalid policy observation shape')
    if any(index < 0 or index >= len(value) for index in excluded_features):
        raise ValueError('Invalid excluded actor feature index')
    value[list(excluded_features)] = 0
    return value
