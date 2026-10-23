import concurrent.futures, json, pathlib, urllib.request, time
root=pathlib.Path(__file__).resolve().parents[1]
out=root/'src/SkinForge.UI/Resources'
base='https://ddragon.leagueoflegends.com'
def fetch(url):
    for attempt in range(3):
        try:
            with urllib.request.urlopen(url, timeout=30) as r: return r.read()
        except Exception:
            if attempt==2: raise
            time.sleep(1)
patch=json.loads(fetch(base+'/api/versions.json'))[0]
data=json.loads(fetch(f'{base}/cdn/{patch}/data/en_US/champion.json'))['data']
def champion(c):
    key=c['id']
    detail=json.loads(fetch(f'{base}/cdn/{patch}/data/en_US/champion/{key}.json'))['data'][key]
    skins=[dict(Id=s['id'],Num=s['num'],Name='Original' if s['num']==0 else s['name'],ChampionId=key,ChampionName=c['name'],ChromaCount=0,Chromas=[],SplashUrl=f"{base}/cdn/img/champion/splash/{key}_{s['num']}.jpg",LoadingUrl=f"{base}/cdn/img/champion/loading/{key}_{s['num']}.jpg",Rarity='',ReleaseInfo='',Source='Riot Data Dragon',Patch=patch) for s in detail['skins']]
    item=dict(Id=key,Key=c['key'],Name=c['name'],Title=c['title'][0].upper()+c['title'][1:],Role=c['tags'][0],Blurb=c['blurb'],IconUrl=f'{base}/cdn/{patch}/img/champion/{key}.png',SplashUrl=f'{base}/cdn/img/champion/splash/{key}_0.jpg',SkinCount=len(skins),Tags=c['tags'])
    image=out/'Artwork'/f'{key}_0.jpg'
    if not image.exists(): image.write_bytes(fetch(f'{base}/cdn/img/champion/loading/{key}_0.jpg'))
    return item,skins
snapshot=dict(Patch=patch,Champions=[],Skins={})
with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
    for c,skins in pool.map(champion,data.values()):
        snapshot['Champions'].append(c); snapshot['Skins'][c['Id']]=skins
snapshot['Champions'].sort(key=lambda c:c['Name'])
# Data Dragon includes chromas as numbered entries, but does not publish a
# distinct splash/loading illustration for each color. Use the parent's real
# illustration and label it explicitly instead of generating broken URLs.
legacy={'Ashe':('Worlds 2017 Ashe Chroma','Worlds 2017 Ashe'),'Bard':('Bard Bloom','Original'),'Blitzcrank':('Blitzcrank Steel','Original'),'Caitlyn':('Caitlyn Rebel','Original'),'Cassiopeia':('Cassiopeia Curse','Original'),'Darius':('Darius Forge','Original'),'Fizz':('Fizz Scorch','Original'),'Garen':('Garen Noble','Original'),'Jax':('Nemesis Jax Sand','Nemesis Jax'),'Karthus':('Grim Reaper Karthus Plague','Grim Reaper Karthus'),'LeeSin':('Dragon Fist Lee Sin Duel','Dragon Fist Lee Sin'),'Leona':('Pool Party Leona Dawn','Pool Party Leona'),'Lucian':('Lucian Prime','Original'),'MasterYi':('Headhunter Master Yi Strike','Headhunter Master Yi'),'Morgana':('Morgana Haunt','Original'),'Nami':('Koi Nami Element','Koi Nami'),'Nasus':('Dreadknight Nasus Plague','Dreadknight Nasus'),'Tristana':('Rocket Girl Tristana Punk','Rocket Girl Tristana'),'Vayne':('Dragonslayer Vayne Chaos','Dragonslayer Vayne'),'Zac':('Zac Sweet','Original')}
for champion,skins in snapshot['Skins'].items():
    canonical=lambda text:' '.join(text.split()).casefold()
    if champion.lower()=='fiddlesticks':
        for skin in skins:
            for field in ['LoadingUrl','SplashUrl']: skin[field]=skin[field].replace('/Fiddlesticks_','/FiddleSticks_')
    by_name={canonical(s['Name']):s for s in skins}
    for skin in skins:
        parent_name=skin['Name'].split(' (')[0] if ' (' in skin['Name'] else None
        if champion in legacy and skin['Name'].startswith(legacy[champion][0]): parent_name=legacy[champion][1]
        parent=by_name.get(canonical(parent_name)) if parent_name else None
        if parent and parent['Id']!=skin['Id']:
            skin['LoadingUrl']=parent['LoadingUrl']
            skin['SplashUrl']=parent['SplashUrl']
            skin['Rarity']='Kroma'
            skin['ReleaseInfo']='Temel kostüm görseli'
(out/'catalog-v2.json').write_text(json.dumps(snapshot,ensure_ascii=False),encoding='utf-8')
hero=next(s for s in snapshot['Skins']['Ahri'] if s['Name']=='Spirit Blossom Ahri')
(out/'Artwork'/'hero.jpg').write_bytes(fetch(hero['SplashUrl']))
print(json.dumps(dict(patch=patch,champions=len(data),skins=sum(map(len,snapshot['Skins'].values())),hero=hero['Num'])))
# Bundle every available skin portrait for fast browsing, including offline.
urls={s['LoadingUrl'] for skins in snapshot['Skins'].values() for s in skins}
def artwork(url):
    image=out/'Artwork'/url.rsplit('/',1)[-1]
    if not image.exists():
        try: image.write_bytes(fetch(url))
        except Exception as e: return (url,str(e))
    return None
with concurrent.futures.ThreadPoolExecutor(max_workers=10) as pool:
    failed=[result for result in pool.map(artwork,sorted(urls)) if result]
(out/'artwork-report.json').write_text(json.dumps(dict(total=len(urls),failed=failed),indent=2),encoding='utf-8')
print(json.dumps(dict(portraits=len(urls),failed=len(failed))))
