MARKET_NAMES=['return5s','return15s','return30s','return60s','spread','obi1','obi5','bookAgeSeconds','logVolume5s','uptickVolumeRatio5s']
BASE_NAMES=[f'{product}_{name}' for product in ['225MC','225M','225'] for name in MARKET_NAMES]+[
    'position','holdingSecondsDiv60','unrealizedTicks','netPnlTicks','drawdownTicks','cooldownSecondsDiv30',
    'pendingEntry','pendingExit','validData','hoursToSessionClose']
EXTRA_NAMES=['ofi5s_mc','ofi5s_mini','ofi5s_large','lead1s_mini_minus_mc','lead1s_large_minus_mc',
    'quote_signed_volume5s_mc','mid_path_vol30s_mc','spread_change5s_mc','holdingMfeTicks','holdingMaeTicks']


PAPER_NAMES=['mc_mlofi5s_level2','mc_mlofi5s_level3','mc_mlofi5s_level4','mc_mlofi5s_level5',
    'mc_log_mean_bid_depth5','mc_log_mean_ask_depth5','mc_bid_depth_derivative5s','mc_ask_depth_derivative5s']


PREDICTIVE_NAMES=['mc_weighted_mid_offset_ticks','mc_near_deep_imbalance_gap','mini_mc_return5s_gap',
    'large_mc_return5s_gap','mc_short_momentum_acceleration','mc_trade_direction_log_volume',
    'mini_mc_obi1_gap','mc_trade_direction_momentum']

POWER_NAMES=['buyPower30s','shortPower60s','longPower180s','biasPower','movementDirection']
POWER_ACTIONS=['Wait','LongSignal','ShortSignal','Flatten','CancelEntry']
RESEARCH_NAMES=['mc_log_visible_depth5','mc_weighted_mid_offset_ticks','mc_gap_asymmetry',
    'mc_sweep5_cost_skew','mc_depth_centroid_asymmetry','mc_depth_spatial_dispersion_ticks',
    'mc_max_depth_position_asymmetry','mc_depth_curvature_asymmetry',
    'mc_ofi5s_depth_normalized','mc_quote_signed_volume5s','mc_log_arrival_rate5s',
    'mc_trade_sign_autocorrelation30s','mc_signed_trade_run_length','mc_trade_size_hhi30s',
    'mc_mid_momentum15s_ticks','mc_signed_path_efficiency30s','mc_log_realized_variance30s',
    'mc_downside_semivariance_share30s','mc_return_autocorrelation30s','mc_abs_return_autocorrelation30s']

def research_exclusions(group):
    included={'baseline':[],'book':list(range(40,48)),'flow':list(range(48,54)),
        'price':list(range(54,60)),'all':list(range(40,60))}
    if group not in included:raise ValueError('Unknown research group')
    return [i for i in range(40,60) if i not in included[group]]

def feature_names(size,profile='three-product'):
    if profile=='mini-micro-research':
        if size!=60:raise ValueError('Research profile requires60inputs')
        return feature_names(40,'mini-micro-power')+RESEARCH_NAMES
    if profile=='mini-micro-power':
        if size!=40:raise ValueError('Power profile requires40inputs')
        return [f'{p}_{n}' for p in ['225MC','225M'] for n in MARKET_NAMES]+[f'{p}_{n}' for p in ['225MC','225M'] for n in POWER_NAMES]+BASE_NAMES[30:]
    if profile!='three-product':raise ValueError('Unknown feature profile')
    if size not in [40,48,50,56]: raise ValueError('Invalid observation size')
    return BASE_NAMES.copy()+(PAPER_NAMES if size in [48,56] else EXTRA_NAMES if size==50 else [])+(PREDICTIVE_NAMES if size==56 else [])
