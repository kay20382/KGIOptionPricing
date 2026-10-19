"""Evidence validation; never upgrades small/development samples to live eligibility."""
import hashlib

ACTION_NAMES=['Wait','EnterLong','EnterShort','Flatten','CancelEntry']

def validate_splits(train, validation):
    if not train or not validation:
        raise ValueError('訓練與驗證區段不可為空')
    a={e.split('-')[0] for e in train};b={e.split('-')[0] for e in validation}
    if a & b or max(a)>=min(b):
        raise ValueError('資料切分重疊或時間順序錯誤')

def verify_manifest(path, manifest):
    if hashlib.sha256(path.read_bytes()).hexdigest().upper()!=manifest['modelSha256'].upper():
        raise ValueError('模型 SHA-256 不符')
    if (manifest['observationSize'] not in [40,48,50,56] or manifest['actionNames']!=ACTION_NAMES
        or manifest.get('schema','nikkei-rl-v1')!={40:'nikkei-rl-v1',48:'nikkei-rl-v3',50:'nikkei-rl-v2',56:'nikkei-rl-v4'}.get(manifest['observationSize'])):
        raise ValueError('模型輸入或動作契約不符')

def build_summary(episodes, usage):
    if any(e['status']!='CompleteEpisode' for e in episodes):
        status='IncompleteExecution'
    elif usage!='PristineHoldout' or len(episodes)<20 or sum(e['completedTrades'] for e in episodes)<200:
        status='IncompleteDevelopmentValidation'
    else:
        status='ResearchEvaluationOnly'
    return dict(status=status, deploymentEligible=False, usage=usage,
                episodeCount=len(episodes), completedTrades=sum(e['completedTrades'] for e in episodes),
                netPnl=sum(e['netPnl'] for e in episodes), costs=sum(e.get('costs',0) for e in episodes),
                maximumEpisodeDrawdown=max((e.get('maxDrawdown',0) for e in episodes),default=0))
