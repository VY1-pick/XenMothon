"""Run with: python -B tools/AdaptiveQualityLab/test_lab.py"""
import dataclasses
import json
import tempfile
from pathlib import Path
from lab import ROOT, display_height, spatial_similarity, Candidate, atomic_json, budgets, candidates, demos, evaluate, geometry_quality, optimize, simulated_oracle, winner


def main():
    source={"width":3840,"height":2160,"fps":30}
    env={"simulation":True,"width":1920,"height":1080,"availableRamMiB":4096,"power":"AC"}
    checks=0
    assert budgets(ram=8192)["ram"]==1024 and budgets(ram=512)["ram"]==128;checks+=1
    locked=candidates(source,True)
    assert all(c.height<=720 and c.fps<=15 and c.scale<=.5 for c in locked);checks+=1
    short={"width":640,"height":360,"fps":12}
    assert all(c.height<=360 and c.fps<=12 for c in candidates(short));checks+=1
    oracle=simulated_oracle(source,env,decode_cpu=220)
    low=Candidate(720,.5,15,False); high=dataclasses.replace(low,fps=30)
    assert oracle(low,6,"screen")["metrics"]["cpuUpper"]>150
    assert oracle(high,6,"screen")["metrics"]["cpuUpper"]>150;checks+=1
    limits=budgets();raw=simulated_oracle(source,env)(low,6,"screen")
    row=evaluate(low,raw,limits,source,env)
    altered=json.loads(json.dumps(raw));altered["metrics"]["cpuUpper"]=None
    missing=evaluate(low,altered,limits,source,env)
    assert not missing["accepted"] and "INSUFFICIENT_CPU_EVIDENCE" in missing["reasons"];checks+=1
    altered=json.loads(json.dumps(raw));altered["metrics"]["colorValid"]=False
    assert "COLOR_INVALID" in evaluate(low,altered,limits,source,env)["reasons"];checks+=1
    changed=dict(row,quality=.99,qualityLower=.99,qualityUpper=.99,cost=.9)
    similar=dict(row,quality=.98,qualityLower=.98,qualityUpper=.98,cost=.3)
    assert winner([changed,similar]) is similar;checks+=1
    assert winner([dict(row,accepted=False)]) is None;checks+=1
    incomplete=dict(similar,coverage=["cpu"],cost=0)
    assert winner([changed,incomplete]) is changed;checks+=1
    results=demos()
    assert results[1]["selected"] and results[1]["economyLocked"];checks+=1
    assert results[2]["status"]=="NoBudgetFit" and results[2]["selected"] is None;checks+=1
    assert results[3]["selected"] and "gpu" not in results[3]["selected"]["coverage"];checks+=1
    assert "CONTAMINATED_BASELINE" in results[5]["reasonCodes"];checks+=1
    assert all(len([c for c in r["candidates"] if c["phase"]=="screen"])<=12 for r in results);checks+=1
    assert all(r["confidence"]!="High" for r in results);checks+=1
    def ram_failure(c,seconds,phase):
        raw=simulated_oracle(source,env)(c,seconds,phase)
        raw["metrics"]["ramMiB"]=1300 if c.hardware else 80
        return raw
    comparison=optimize(source,env,ram_failure,max_trials=4,validate=False)
    assert comparison["candidates"][1]["settings"]["useHardwareDecoding"] is False;checks+=1
    assert geometry_quality(Candidate(1080,1,30),{"width":1920,"height":1080,"fps":24},env)==geometry_quality(Candidate(1080,1,24),{"width":1920,"height":1080,"fps":24},env);checks+=1
    altered=json.loads(json.dumps(raw));altered["metrics"].update(ramMiB=82,privateCommitMiB=1151)
    assert evaluate(low,altered,limits,source,env)["accepted"];checks+=1
    altered["metrics"]["ramMiB"]=1025
    assert "OVER_BUDGET:ramMiB" in evaluate(low,altered,limits,source,env)["reasons"];checks+=1
    altered["metrics"].update(ramMiB=82,privateCommitMiB=2049)
    assert "OVER_BUDGET:privateCommitMiB" in evaluate(low,altered,limits,source,env)["reasons"];checks+=1
    altered=json.loads(json.dumps(raw));altered["metrics"]["submitMissRatio"]=.01;altered["phase"]="screen"
    assert evaluate(low,altered,limits,source,env)["accepted"];checks+=1
    altered["phase"]="validate"
    assert not evaluate(low,altered,limits,source,env)["accepted"];checks+=1
    def failed_replay(c,seconds,phase):
        evidence=simulated_oracle(source,env)(c,seconds,phase)
        if phase=="validate": evidence["metrics"]["submitMissRatio"]=.5
        return evidence
    rejected=optimize(source,env,failed_replay,max_trials=1)
    assert rejected["selected"] is None and rejected["status"]=="NoBudgetFit";checks+=1
    def option(height,cost,scale=1):
        return dict(row,id=str(height)+str(scale),settings=Candidate(height,scale,30).settings(),
                    qualityLower=.98,qualityUpper=1,quality=.99,cost=cost,resourceRatios={"cpu":cost})
    native=option(1080,.43);smaller=option(720,.40)
    assert winner([native,smaller],source,env) is native;checks+=1
    env720=dict(env,width=1280,height=720)
    native720=option(720,.43);larger=option(1080,.40)
    assert winner([native720,larger],source,env720) is native720;checks+=1
    expensive=option(1080,.60)
    assert winner([expensive,smaller],source,env) is expensive;checks+=1
    spike=dict(native,resourceRatios={"cpu":.60})
    assert winner([spike,smaller],source,env) is spike;checks+=1
    bad=dict(native,accepted=False)
    assert winner([bad,smaller],source,env) is smaller;checks+=1
    reduced=option(1080,.40,.5)
    assert winner([native,reduced],source,env) is native;checks+=1
    assert winner([option(720,.40),option(1080,.43)],dict(source,height=720,width=1280),env)["settings"]["videoHeightLimit"]==720;checks+=1
    capped=optimize(source,env,simulated_oracle(source,env),max_trials=4,validate=False,caps=(720,15,.5,False))
    assert all(r["settings"]["videoHeightLimit"]<=720 and r["settings"]["targetFps"]<=15 and r["settings"]["renderScale"]<=.5 for r in capped["candidates"]);checks+=1
    software=optimize(source,env,simulated_oracle(source,env),max_trials=2,validate=False,caps=(1080,30,1,True))
    assert all(not r["settings"]["useHardwareDecoding"] for r in software["candidates"]);checks+=1
    display4k=dict(env,width=3840,height=2160)
    bounded=optimize(source,display4k,simulated_oracle(source,display4k),max_trials=4,validate=False,caps=(2160,60,1,False))
    assert all(r["settings"]["videoHeightLimit"]>=1440 and r["settings"]["renderScale"]>=.85 for r in bounded["candidates"]);checks+=1
    fractional=dict(source,fps=23.976)
    assert max(c.fps for c in candidates(fractional))==24;checks+=1
    assert geometry_quality(Candidate(1080,1,24),fractional,env)==geometry_quality(Candidate(1080,1,60),fractional,env);checks+=1
    portrait={"width":1000,"height":3000,"fps":30}
    assert display_height(portrait,env)==3000;checks+=1
    ultra=dict(env,width=3440,height=1440)
    assert display_height(source,ultra)==1935;checks+=1
    assert geometry_quality(Candidate(1935,1,30),source,ultra)>geometry_quality(Candidate(1440,1,30),source,ultra);checks+=1
    test_root=ROOT/"artifacts/adaptive-quality-lab"
    test_root.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="quality-lab-check-",dir=test_root) as directory:
        path=Path(directory)/"result.json";atomic_json(path,{"first":1});atomic_json(path,{"second":2})
        assert json.loads(path.read_text())=={"second":2} and not path.with_suffix(".json.tmp").exists();checks+=1
    pixels=[((x*13)%255)|(((x*7)%255)<<8)|(((x*3)%255)<<16) for x in range(256)]
    assert abs(spatial_similarity(pixels,pixels,16,16)-1)<1e-9;checks+=1
    assert spatial_similarity(pixels,[0]*256,16,16)<.9;checks+=1
    print(f"PASS: {checks} behavioral checks; no application settings touched.")


if __name__=="__main__":main()
