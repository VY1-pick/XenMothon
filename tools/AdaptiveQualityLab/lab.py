"""Independent optimizer lab: deterministic scenarios, imported evidence, and native video probes.
No writes to XenMotion settings, source media, registry, or wallpaper assignment.
"""
from __future__ import annotations

import argparse
import dataclasses
from functools import lru_cache
import hashlib
import json
import math
import os
from pathlib import Path
import random
import statistics
import subprocess
import time

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
VERSION = "quality-lab-2.3"


@dataclasses.dataclass(frozen=True)
class Candidate:
    height: int
    scale: float
    fps: int
    filtering: bool = True
    sharpness: float = 0.0
    hardware: bool = True

    @property
    def key(self):
        return f"{self.height}p/{self.fps}fps/{round(self.scale*100)}%/{'mip' if self.filtering else 'linear'}/s{self.sharpness:g}/{'shared' if self.hardware else 'CPU'}"

    def settings(self):
        return {"videoHeightLimit": self.height, "renderScale": self.scale,
                "targetFps": self.fps, "highQualityFiltering": self.filtering,
                "sharpness": self.sharpness, "useHardwareDecoding": self.hardware}


def percentile(values, q=.95):
    values = sorted(v for v in values if isinstance(v, (int, float)) and math.isfinite(v))
    return None if not values else values[max(0, math.ceil(len(values) * q) - 1)]


def upper_mean(values):
    """Operational block bootstrap, not a universal statistical confidence guarantee."""
    values = [v for v in values if isinstance(v, (int, float)) and math.isfinite(v)]
    if not values:
        return None
    rng = random.Random(17)
    means = [statistics.fmean(rng.choices(values, k=len(values))) for _ in range(200)]
    return percentile(means)


def budgets(power="AC", ram=4096, commit=8192):
    dc = power == "DC"
    return {"cpu": 80 if dc else 150, "cpuP95": 150 if dc else 250,
            "gpu": 8 if dc else 15, "gpuP95": 15 if dc else 25,
            "ram": min(1024, ram * .25), "commit": min(2048, commit * .25),
            "local": .35 if dc else .5, "nonLocal": .35 if dc else .5,
            # Proxy only when engine busy is unavailable: no fabricated utilization percentage.
            "gpuRenderProxyMsPerSec": 80 if dc else 150}


def candidates(source, economy=False):
    h, fps = source["height"], min(240, max(1, math.ceil(source["fps"])))
    if economy:
        return [Candidate(min(h, 720), .5, min(fps, 15), f, s, hw)
                for hw in (True, False) for f, s in ((False, 0), (False, .1), (True, 0), (True, .1), (True, .15))]
    out = []
    for height, scale, rate in ((720,.5,15),(1080,.75,24),(1440,1,30),(h,1,fps)):
        c = Candidate(min(h, height), scale, min(fps, rate), height > 720)
        out.extend([c, dataclasses.replace(c, filtering=True, sharpness=.1), dataclasses.replace(c, hardware=False)])
    for height in sorted(set([min(h,x) for x in (720,1080,1440,h)])):
        for scale in (.5,.625,.75,.875,1):
            for rate in sorted(set(min(fps,x) for x in (15,20,24,25,30,40,48,50,60,fps))):
                out.append(Candidate(height,scale,rate,height>720))
    return list(dict.fromkeys(out))


def display_height(source, environment):
    # Fill/Cover uses the larger scale factor; min() would undersample portrait/ultrawide crops.
    return min(source["height"],max(environment["height"],environment["width"]*source["height"]/source["width"]))


def geometry_quality(c, source, environment, motion="medium"):
    spatial=min(1,c.height/display_height(source,environment),c.scale)**.36
    temporal = min(1, c.fps / source["fps"]) ** .5
    ws, wt = {"low":(.65,.2),"medium":(.525,.325),"high":(.4,.45)}[motion]
    return ws * spatial + wt * temporal + .15


def evaluate(c, raw, limits, source, env, motion="medium"):
    if raw.get("error"):
        return {"id":c.key,"settings":c.settings(),"accepted":False,"reasons":[raw["error"]],
                "quality":None,"qualityLower":None,"cost":None,"coverage":[],"metrics":{},"phase":raw.get("phase","screen")}
    metrics = raw.get("metrics", raw)
    reasons, ratios, missing, warnings = [], {}, [], []
    rules = [("cpu","cpuUpper",limits["cpu"]),("cpuP95","cpuP95",limits["cpuP95"]),
             ("gpu","gpuBusy",limits["gpu"]),("gpuP95","gpuP95",limits["gpuP95"]),
             ("ram","ramMiB",limits["ram"]),("commit","privateCommitMiB",limits["commit"]),("local","localRatio",limits["local"]),
             ("nonLocal","nonLocalRatio",limits["nonLocal"])]
    for dimension, key, ceiling in rules:
        value = metrics.get(key)
        if value is None:
            missing.append(key)
            continue
        if not isinstance(value,(int,float)) or not math.isfinite(value) or value < 0:
            reasons.append("INVALID_METRIC:"+key); continue
        ratios[dimension] = value / max(.001, ceiling)
        if value > ceiling: reasons.append("OVER_BUDGET:"+key)
    gpu_proxy = metrics.get("gpuRenderMsPerSec")
    if metrics.get("gpuBusy") is None and gpu_proxy is not None:
        ratios["gpuRenderProxy"] = gpu_proxy / limits["gpuRenderProxyMsPerSec"]
        if ratios["gpuRenderProxy"] > 1: warnings.append("ADVISORY:gpuRenderProxy")
    for key in ("missRatio", "submitMissRatio"):
        value = metrics.get(key)
        if value is not None and value > .005:
            if key=="submitMissRatio" and raw.get("phase")!="validate": warnings.append("SCREEN_TIMING_NOISE:"+key)
            else: reasons.append("DEADLINES:"+key)
    if metrics.get("colorValid") is False: reasons.append("COLOR_INVALID")
    if metrics.get("audioUnderruns",0): reasons.append("AUDIO_UNDERRUN")
    if metrics.get("audioDriftMs") is not None and abs(metrics["audioDriftMs"]) > 40: reasons.append("AUDIO_DRIFT")
    if metrics.get("maxLateMs") is not None and metrics["maxLateMs"] > max(100,2000/c.fps): reasons.append("LONG_STALL")
    if metrics.get("cpuUpper") is None: reasons.append("INSUFFICIENT_CPU_EVIDENCE")
    quality = raw.get("quality")
    if quality is None:
        quality = geometry_quality(c,source,env,motion)
        missing.append("measuredQuality")
    if not math.isfinite(quality) or not 0 <= quality <= 1: raise ValueError("Invalid quality score")
    uncertainty = raw.get("qualityUncertainty",.04)
    return {"id":c.key,"settings":c.settings(),"accepted":not reasons,"reasons":reasons,
            "quality":quality,"qualityLower":max(0,quality-uncertainty),
            "qualityUpper":min(1,quality+uncertainty),"cost":max(ratios.values(),default=None),
            "resourceRatios":ratios,"coverage":sorted(ratios),"missing":missing,"warnings":warnings,"metrics":metrics,
            "phase":raw.get("phase","screen"),"sampleCount":raw.get("sampleCount",0)}


def dominates(a,b):
    if a["coverage"] != b["coverage"]: return False
    return (a["qualityLower"] >= b["qualityUpper"] and
            a["cost"] <= b["cost"] and
            (a["qualityLower"] > b["qualityUpper"] or a["cost"] < b["cost"]))


def front(rows):
    accepted = [r for r in rows if r["accepted"] and r["cost"] is not None]
    return [a for a in accepted if not any(dominates(b,a) for b in accepted if a is not b)]


def winner(rows, source=None, env=None):
    eligible = [r for r in rows if r["accepted"] and r["cost"] is not None] if source and env else front(rows)
    if not eligible: return None
    # Unknown metrics never get a zero-cost advantage over measured dimensions.
    groups = {}
    for row in eligible: groups.setdefault(tuple(row["coverage"]),[]).append(row)
    coverage = max(groups,key=lambda keys:(len(keys),keys))
    eligible = groups[coverage]
    if source and env:
        target=display_height(source,env)
        def fit(row):
            settings=row["settings"]
            effective=min(settings["videoHeightLimit"],target*settings["renderScale"])
            return (abs(math.log(max(1,effective)/target)),
                    abs(math.log(max(1,settings["videoHeightLimit"])/target)),
                    -row["qualityLower"],row["cost"],row["id"])
        return min(eligible,key=fit)
    best = max(r["qualityLower"] for r in eligible)
    near = [r for r in eligible if r["qualityLower"] >= best-.02-1e-9]
    return min(near,key=lambda r:(r["cost"],r["metrics"].get("openMs",math.inf),r["id"]))


def features(c,source):
    return [1,(c.height/source["height"])**2,c.scale**2*c.fps/30,
            float(c.filtering)*c.scale**2*c.fps/30,float(not c.hardware)]


@lru_cache(maxsize=32)
def fit_cost(training):
    weights = [0.0]*len(training[0][0])
    for _ in range(60):
        for j in range(len(weights)):
            numerator=sum(f[j]*(y-sum(w*v for k,(w,v) in enumerate(zip(weights,f)) if k!=j)) for f,y in training)
            denominator=sum(f[j]**2 for f,_ in training)+.01
            weights[j]=max(0,numerator/denominator)
    error=statistics.fmean(abs(y-sum(w*v for w,v in zip(weights,f))) for f,y in training)
    return weights,error


def predict_cost(c, observed, source):
    training = [(features(candidate,source),row["cost"]) for candidate,row in observed
                if row.get("cost") is not None and math.isfinite(row["cost"])]
    x = features(c,source)
    if len(training) < 6:
        if not training:return .6,.8
        nearest = min(training,key=lambda row:sum((a-b)**2 for a,b in zip(x,row[0])))
        distance = math.sqrt(sum((a-b)**2 for a,b in zip(x,nearest[0])))
        return nearest[1],.15+distance*.3
    # Fit once per observation set, not once for every candidate being ranked.
    weights,error=fit_cost(tuple((tuple(f),y) for f,y in training))
    distance=min(math.sqrt(sum((a-b)**2 for a,b in zip(x,f))) for f,_ in training)
    return sum(w*v for w,v in zip(weights,x)),max(.1,error*2+distance*.2)


def optimize(source,env,oracle,*,economy=False,motion="medium",max_trials=12,deadline=None,validate=True,ram_budget=None,reference_candidate=None,caps=None):
    limits=budgets(env.get("power","AC"),env.get("availableRamMiB") or 4096,env.get("availableCommitMiB") or 8192)
    if ram_budget is not None: limits["ram"]=ram_budget
    space=candidates(source,economy);
    if reference_candidate is not None and reference_candidate not in space and not economy:space.append(reference_candidate)
    monitor_candidate=Candidate(max(1,math.ceil(display_height(source,env))),1,min(240,max(1,math.ceil(source["fps"]))))
    if not economy and monitor_candidate not in space:space.append(monitor_candidate)
    if caps:
        if caps[3]: space=list(dict.fromkeys(dataclasses.replace(c,hardware=False) for c in space))
        space=[c for c in space if c.height<=caps[0] and c.fps<=caps[1] and c.scale<=caps[2] and (not caps[3] or not c.hardware)]
        # Production never drops more than one resolution tier below the display target.
        target=min(source["height"],monitor_candidate.height,caps[0])
        floor=max([h for h in (720,1080,1440,2160) if h<target],default=min(720,target))
        space=[c for c in space if c.height>=floor and c.scale>=min(.85,caps[2])]
        if not space: raise ValueError("No candidates within requested limits")
        if reference_candidate not in space: reference_candidate=None
        monitor_candidate=min(space,key=lambda c:abs(c.height-monitor_candidate.height)+abs(c.scale-1)*1000)
    observations=[]; rows=[]; events=[]; used=set()
    middle=min(space,key=lambda c:abs(c.height-min(source["height"],1080))/1080+abs(c.scale-.75)+abs(c.fps-min(source["fps"],24))/30+float(not c.hardware))
    seeds=list(dict.fromkeys(([monitor_candidate] if not economy else [])+([reference_candidate] if reference_candidate is not None and not economy else [])+[space[0],middle]+[max(space,key=lambda c:geometry_quality(c,source,env,motion))]))
    flat=0; last_best=0
    for trial in range(max_trials):
        if deadline and time.monotonic()>deadline-58:break
        remaining=[c for c in space if c not in used]
        if not remaining:break
        cpu_floor=next((c for c in remaining if not c.hardware and c.height==space[0].height and c.fps==space[0].fps and c.scale==space[0].scale),None)
        if rows and any("ramMiB" in reason for reason in rows[-1]["reasons"]) and all(c.hardware for c,_ in observations) and cpu_floor:
            c=cpu_floor;rationale="memory bottleneck: compare minimum CPU-output decoder path before increasing pixels"
        elif any(seed not in used for seed in seeds):
            c=next(seed for seed in seeds if seed not in used); rationale="seed: minimum / middle / quality ceiling"
        else:
            scored=[]
            for c in remaining:
                predicted,uncertainty=predict_cost(c,observations,source)
                q=geometry_quality(c,source,env,motion)
                # Penalize predicted excessive cost; keep uncertainty exploration but never auto-approve it.
                score=q-max(0,predicted-1)*.35-uncertainty*.04
                if trial in (4,8,11):score+=uncertainty*.12
                if observations and rows[-1].get("reasons") and any("cpu" in x for x in rows[-1]["reasons"]):
                    score+=.08 if c.hardware else 0
                scored.append((score,c.key,c))
            c=max(scored,key=lambda entry:entry[:2])[2];rationale="bottleneck-aware predicted gain + bounded exploration"
        used.add(c)
        raw=oracle(c,6,"screen")
        row=evaluate(c,raw,limits,source,env,motion)
        observations.append((c,row));rows.append(row)
        events.append({"stage":"S09/S10","candidate":c.key,"decision":rationale,
                       "accepted":row["accepted"],"reasons":row["reasons"]})
        current=winner(rows,source,env)
        best=current["qualityLower"] if current else 0
        if trial>=3:
            flat=flat+1 if best<=last_best+.005 else 0
        last_best=max(last_best,best)
        # Finish all three seeds and some model evidence before the no-gain stop.
        if flat>=2 and trial>=7:break
    selected=winner(rows,source,env)
    verified=[]
    if validate and selected:
        candidate_map={c.key:c for c in space}
        contenders=sorted(front(rows),key=lambda r:(-r["qualityLower"],r["cost"]))
        finalists=[selected]+[r for r in contenders if r["id"]!=selected["id"]][:1]
        for row in finalists:
            if deadline and time.monotonic()>deadline-29:break
            c=candidate_map[row["id"]]
            raw=oracle(c,24,"validate")
            v=evaluate(c,raw,limits,source,env,motion);rows.append(v);verified.append(v)
            events.append({"stage":"S13","candidate":c.key,"decision":"independent longer replay",
                           "accepted":v["accepted"],"reasons":v["reasons"]})
        selected=winner(verified,source,env)
    failed_validation={r["id"] for r in verified if not r["accepted"]}
    provisional=selected or winner([r for r in rows if r["phase"]=="screen" and r["id"] not in failed_validation],source,env)
    if selected is None and provisional:
        status="Provisional";confidence="Low";reasons=["LONG_VALIDATION_NOT_PASSED"]
    elif selected:
        synthetic=env.get("simulation",False)
        status="Simulation" if synthetic else "Provisional"
        confidence="Synthetic" if synthetic else "Low"
        reasons=[] if synthetic else ["DISPLAY_TIMING_UNVERIFIED","QUALITY_PROXY_ONLY","AUDIO_UNTESTED","CODEC_FAMILY_UNVERIFIED"]
    else:
        status="NoBudgetFit";confidence="Low";reasons=["NO_ACCEPTABLE_ANIMATED_CANDIDATE"]
    if env.get("baselineCpuPercent",0) is not None and env.get("baselineCpuPercent",0)>25:
        reasons.append("CONTAMINATED_BASELINE");confidence="Low"
        if provisional:status="Provisional"
    return {"schemaVersion":1,"algorithmVersion":VERSION,"status":status,"confidence":confidence,
            "reasonCodes":reasons,"source":source,"environment":env,"budgets":limits,
            "selected":provisional,"candidates":rows,"events":events,
            "paretoIds":[r["id"] for r in front(rows)],"economyLocked":economy,
            "motionAssumption":motion,"applicationModified":False,
            "selectionPolicy":{"nearQuality":.02,"nearResourceBudgetFraction":.05,"preferMonitorFit":True},
            "policyOverrides":{} if ram_budget is None else {"ramMiB":ram_budget},
            "scope":"single primary display / selected source" if not env.get("simulation") else "synthetic scenario",
            "unimplemented":["full installer integration","independent multi-monitor allocation","AC/DC cross-context validation",
                             "full multiscale image-quality suite","physical display timing","audio and power telemetry"]}


def simulated_oracle(source,env,*,cpu_base=25,gpu_factor=1,decode_cpu=0,missing_gpu=False):
    def measure(c,seconds,phase):
        area=(c.height/source["height"])**2
        cpu=cpu_base+decode_cpu+(10 if c.hardware else 220)+20*area+10*c.scale*c.fps/30
        gpu=gpu_factor*(1+8*c.scale**2*c.fps/30+3*area)*(1.15 if c.filtering else 1)
        count=round(seconds*c.fps)
        q=geometry_quality(c,source,env)+(0.012 if c.filtering else 0)+(.006 if c.sharpness==.1 else 0)
        return {"metrics":{"cpuUpper":cpu*1.02,"cpuP95":cpu*1.05,"gpuBusy":None if missing_gpu else gpu,
            "gpuP95":None if missing_gpu else gpu*1.05,"gpuRenderMsPerSec":gpu*6 if missing_gpu else None,
            "ramMiB":40+90*area+40*c.scale**2,"localRatio":.01+.06*c.scale**2,
            "nonLocalRatio":.002,"missRatio":0,"colorValid":True,"audioUnderruns":0,"audioDriftMs":0},
            "quality":min(1,q),"qualityUncertainty":.002,"sampleCount":count,"phase":phase}
    return measure


def demos():
    source={"name":"Synthetic 4K/30","width":3840,"height":2160,"fps":30}
    base={"simulation":True,"power":"AC","availableRamMiB":4096,"width":1920,"height":1080,"baselineCpuPercent":5}
    configs=[("سیستم قوی — دادهٔ شبیه‌سازی",{},{}),
             ("سیستم ضعیف با سقف Economy",{}, {"economy":True,"gpu_factor":3.5}),
             ("گلوگاه دیکود — FPS درمان نیست",{}, {"decode_cpu":220}),
             ("شمارندهٔ GPU ناموجود",{}, {"missing_gpu":True}),
             ("حالت باتری",{"power":"DC"},{"gpu_factor":1.3}),
             ("بار محیط آلوده",{"baselineCpuPercent":55},{})]
    results=[]
    for name,delta,params in configs:
        env=base|delta; economy=params.pop("economy",False)
        r=optimize(source,env,simulated_oracle(source,env,**params),economy=economy)
        r["name"]=name;results.append(r)
    return results


def atomic_json(path,data):
    path.parent.mkdir(parents=True,exist_ok=True)
    temporary=path.with_suffix(path.suffix+".tmp")
    with temporary.open("w",encoding="utf-8") as f:
        json.dump(data,f,ensure_ascii=False,indent=2,allow_nan=False);f.flush();os.fsync(f.fileno())
    os.replace(temporary,path)


def probe(exe,video,output,request,timeout=40):
    request_path=output.with_suffix(".request.json")
    atomic_json(request_path,{"video":str(video),**request})
    try:
        p=subprocess.run([str(exe),str(request_path),str(output)],capture_output=True,timeout=timeout)
        data=json.loads(output.read_text(encoding="utf-8")) if output.exists() else {"error":"WORKER_FAILED"}
        if p.returncode and "error" not in data:data["error"]="WORKER_FAILED"
        return data
    except subprocess.TimeoutExpired:
        return {"error":"WORKER_TIMEOUT"} # subprocess kills/reaps only this isolated child.


def spatial_similarity(colors,reference,width,height):
    def luma(v):return (.2126*(v&255)+.7152*((v>>8)&255)+.0722*((v>>16)&255))/255
    a,b=list(map(luma,colors)),list(map(luma,reference))
    scores=[]
    for y in range(0,height-7,8):
        for x in range(0,width-7,8):
            av=[a[(y+j)*width+x+i] for j in range(8) for i in range(8)]
            bv=[b[(y+j)*width+x+i] for j in range(8) for i in range(8)]
            ma,mb=statistics.fmean(av),statistics.fmean(bv)
            va=statistics.fmean((v-ma)**2 for v in av);vb=statistics.fmean((v-mb)**2 for v in bv)
            cov=statistics.fmean((v-ma)*(w-mb) for v,w in zip(av,bv))
            scores.append(max(0,min(1,((2*ma*mb+.0001)*(2*cov+.0009))/((ma*ma+mb*mb+.0001)*(va+vb+.0009)))))
    return .75*statistics.fmean(scores)+.25*(percentile(scores,.1) or 0)


def quality_proxy(frames,reference,c,source,env,motion,width=256,height=144):
    if not frames or not reference:return None
    scores=[]
    for f in frames:
        r=min(reference,key=lambda item:abs(item["timestamp"]-f["timestamp"]))
        if abs(r["timestamp"]-f["timestamp"])>0.5/source["fps"]:continue
        if len(f["colors"])!=width*height or len(r["colors"])!=width*height:continue
        scores.append(spatial_similarity(f["colors"],r["colors"],width,height))
    if len(scores)<2:return None
    spatial=.75*statistics.fmean(scores)+.25*min(scores)
    temporal=min(1,c.fps/source["fps"])**.5
    ws,wt={"low":(.65,.2),"medium":(.525,.325),"high":(.4,.45)}[motion]
    return ws*spatial+wt*temporal+.15


def native_run(args):
    output=args.output.resolve(); output.mkdir(parents=True,exist_ok=True)
    exe=args.probe.resolve();video=args.video.resolve()
    if not exe.is_file() or not video.is_file():raise ValueError("Probe executable or video is missing")
    inspected=probe(exe,video,output/"inspect.json",{"inspect":True},20)
    if inspected.get("error"):raise ValueError(inspected["error"])
    source,env=inspected["source"],inspected["environment"]
    if args.width: env["width"],env["height"]=args.width,args.height
    if not env["interactive"] or env["remote"]:
        return {"name":"تست واقعی: محیط نامناسب","status":"Deferred","confidence":"Low","reasonCodes":["NON_LOCAL_INTERACTIVE_SESSION"],"source":source,"environment":env,"candidates":[],"selected":None}
    if (env.get("baselineCpuPercent") or 0)>25 and not args.observe_busy:
        return {"name":"تست واقعی: بار سیستم بالا","status":"Deferred","confidence":"Low","reasonCodes":["BUSY_BASELINE"],"source":source,"environment":env,"candidates":[],"selected":None}
    deadline=time.monotonic()+args.budget
    common={"width":env["width"],"height":env["height"]}
    saved=None
    if args.settings_file:
        saved=json.loads(args.settings_file.read_text(encoding="utf-8-sig"))
    reference_candidate=Candidate(min(source["height"], (saved or {}).get("VideoHeightLimit") or source["height"]),
        (saved or {}).get("RenderScale",1), min(math.ceil(source["fps"]),(saved or {}).get("GlobalTargetFps",math.ceil(source["fps"]))),
        (saved or {}).get("HighQualityFiltering",True), (saved or {}).get("DefaultSharpness") or 0,
        (saved or {}).get("UseHardwareDecoding",True))
    print("Native reference: full source / full render",flush=True)
    reference=probe(exe,video,output/"reference.json",common|{"processingHeight":0,"scale":1,"fps":min(30,math.ceil(source["fps"])),"seconds":1},min(20,args.budget))
    reference_colors=reference.get("qualityFrames")
    index=0
    def oracle(c,seconds,phase):
        nonlocal index
        index+=1
        print(f"{phase} #{index}: {c.key} ({seconds}s)",flush=True)
        request=common|{"processingHeight":c.height,"scale":c.scale,"fps":c.fps,"filter":c.filtering,
                        "sharpness":c.sharpness,"hardware":c.hardware,"seconds":seconds}
        raw=probe(exe,video,output/f"probe-{index:02}.json",request,max(1,min(seconds+15,deadline-time.monotonic())))
        if raw.get("error"):return raw|{"phase":phase}
        memory=raw.get("memory") or {}
        def ratio(segment):
            v=memory.get(segment)
            return None if not v or v.get("budgetMiB",0)<=0 else v["usageMiB"]/v["budgetMiB"]
        gpu_p95=percentile(raw["gpuRenderMs"])
        metrics={"cpuUpper":upper_mean(raw["cpuWindows"]) if raw["cpuWindows"] else raw["cpuCoreMsPerSec"],
                 "cpuMean":raw["cpuCoreMsPerSec"],"cpuP95":percentile(raw["cpuWindows"]),
                 "gpuBusy":None,"gpuP95":None,"gpuRenderP95Ms":gpu_p95,
                 "gpuRenderMsPerSec":None if gpu_p95 is None else gpu_p95*min(c.fps,source["fps"]),
                 "ramMiB":max([raw["workingSetPeakMiB"]]+[s["workingSetMiB"] for s in raw["memoryStages"]]),
                 "privateCommitMiB":max([raw["totalPrivateMiB"]]+[s["privateMiB"] for s in raw["memoryStages"]]),
                 "privateAddedMiB":raw["privateAddedMiB"],"totalPrivateMiB":raw["totalPrivateMiB"],
                 "workingSetPeakMiB":raw["workingSetPeakMiB"],"memoryStages":raw["memoryStages"],
                 "localRatio":ratio("local"),"nonLocalRatio":ratio("nonLocal"),
                 "submitMissRatio":raw["submitMissRatio"],"missRatio":None,
                 "maxLateMs":raw["maxSubmitLateMs"],"decodeWallP95Ms":percentile(raw["decodeWallMs"]),
                 "openMs":raw["openMs"],"gpuResidentRatio":raw["gpuResidentOutputFrames"]/max(1,raw["decodedFrames"]),
                 "decodedFrames":raw["decodedFrames"],"submittedFrames":raw["submittedFrames"],
                 "measuredSeconds":raw["measuredSeconds"],"adapter":memory.get("adapter")}
        q=quality_proxy(raw.get("qualityFrames"),reference_colors,c,source,env,args.motion)
        if phase=="validate" and raw["measuredSeconds"]<seconds*.9:
            return {"error":"INSUFFICIENT_VALIDATION_DURATION","phase":phase}
        print(f"  CPU={metrics['cpuMean']:.1f} core-ms/s, GPU p95={gpu_p95}, missed={raw['submitMissRatio']:.3%}",flush=True)
        return {"metrics":metrics,"quality":q,"qualityUncertainty":.02,"phase":phase,"sampleCount":raw["submittedFrames"]}
    result=optimize(source,env,oracle,economy=args.economy,motion=args.motion,deadline=deadline,ram_budget=args.ram_budget_mib,reference_candidate=reference_candidate, caps=(min(source["height"],args.height_limit or source["height"]),args.fps_limit,args.scale_limit,args.software_only) if args.production else None)
    result["name"]="تست واقعی ویدیوی 4K روی این سیستم"
    result["elapsedSeconds"]=args.budget-max(0,deadline-time.monotonic())
    result["referenceCandidate"]=reference_candidate.key
    result["referenceSettingsSource"]="saved preferences (effective live settings not observed)" if saved else "original source baseline"
    result["referenceValid"]=bool(reference_colors)
    result["fingerprint"]=hashlib.sha256(json.dumps([VERSION,source,env.get("os"),env.get("width"),env.get("height"),video.stat().st_size,video.stat().st_mtime_ns],sort_keys=True).encode()).hexdigest()
    return result


def render_report(results,path):
    template=(HERE/"report-template.html").read_text(encoding="utf-8")
    embedded=json.dumps({"schemaVersion":1,"results":results},ensure_ascii=False,allow_nan=False).replace("<","\\u003c")
    path.write_text(template.replace("__REPORT_DATA__",embedded),encoding="utf-8")


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--production",action="store_true",help="Use measured video only; honor limits; erase raw frame samples after the report")
    p.add_argument("--width",type=int)
    p.add_argument("--height",type=int)
    p.add_argument("--height-limit",type=int,default=0)
    p.add_argument("--fps-limit",type=int,default=240)
    p.add_argument("--scale-limit",type=float,default=1)
    p.add_argument("--software-only",action="store_true")
    p.add_argument("--video",type=Path)
    p.add_argument("--settings-file",type=Path,help="Read-only saved playback reference")
    p.add_argument("--probe",type=Path,default=ROOT/"artifacts/adaptive-quality-lab/probe/NativeProbe.exe")
    p.add_argument("--output",type=Path,default=ROOT/"artifacts/adaptive-quality-lab")
    p.add_argument("--economy",action="store_true")
    p.add_argument("--observe-busy",action="store_true",help="Measure despite busy CPU baseline; marks result contaminated/provisional")
    p.add_argument("--motion",choices=["low","medium","high"],default="medium")
    p.add_argument("--budget",type=int,default=180)
    p.add_argument("--ram-budget-mib",type=float,help="Explicit experimental memory-policy override; default remains the design budget")
    args=p.parse_args()
    if not 80<=args.budget<=180:p.error("Budget must be 80..180 seconds")
    if args.ram_budget_mib is not None and not 1<=args.ram_budget_mib<=8192:p.error("Memory budget must be 1..8192 MiB")
    if bool(args.width)!=bool(args.height) or (args.width and not (32<=args.width<=16384 and 32<=args.height<=16384 and args.width*args.height<=40000000)):p.error("Invalid display bounds")
    if not (0<=args.height_limit<=16384 and 1<=args.fps_limit<=240 and math.isfinite(args.scale_limit) and .25<=args.scale_limit<=1):p.error("Invalid quality limits")
    if args.production and not args.video:p.error("Production tuning requires a video")
    results=[] if args.production else demos()
    if args.video:
        try:results.insert(0,native_run(args))
        except (ValueError,OSError,KeyboardInterrupt) as e:
            results.insert(0,{"name":"تست واقعی: متوقف یا ناموفق","status":"Failed","confidence":"Low","reasonCodes":[type(e).__name__],"candidates":[],"selected":None})
    args.output.mkdir(parents=True,exist_ok=True)
    atomic_json(args.output/"results.json",{"schemaVersion":1,"results":results})
    render_report(results,args.output/"report.html")
    if args.production:
        for item in args.output.iterdir():
            if item.is_file() and (item.name in ("reference.json","reference.request.json","reference-settings.json","inspect.json","inspect.request.json") or (item.name.startswith("probe-") and item.name.endswith(".json"))): item.unlink()
    for r in results:
        selected=r.get("selected")
        print(f"{r['status']}: {selected['id'] if selected else 'no animated candidate'}",flush=True)
    print("Report: "+str((args.output/"report.html").resolve()),flush=True)


if __name__=="__main__":main()
