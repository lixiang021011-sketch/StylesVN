#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
《斯泰尔斯庄园奇案》美术生产线
ComfyUI + ControlNet（Union SDXL）+ IPAdapter（角色一致性）+ Lightning LoRA（4~8 步出图）

流程：
    ToolsOut/asset_manifest.json  →  ［本脚本］  →  Assets/Art/{Backgrounds,Characters}/*.png
                          ▲                                    │
                   blockout 构图规格 ──► ControlNet 引导图 ──┘

用法：
    python tools/comfy/pipeline.py --kind bg --only styles_bedroom styles_library
    python tools/comfy/pipeline.py --kind char --limit 3
    python tools/comfy/pipeline.py --kind bg --all
"""
import argparse, json, os, sys, time, urllib.parse, urllib.request, io, random, math

COMFY = os.environ.get("COMFY_URL", "http://127.0.0.1:8188")
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
# 必须放在 Resources 下：运行时代码用 Resources.Load<Sprite>("Art/...") 读取，否则打包不会收录
ART = os.path.join(ROOT, "Assets", "Resources", "Art")
OUT_GUIDE = os.path.join(ROOT, "ToolsOut", "control")
OUT_MANIFEST = os.path.join(ROOT, "ToolsOut", "asset_manifest.json")

CKPT_BG = os.environ.get("CKPT_BG", "sd_xl_base_1.0.safetensors")
CKPT_CHAR = os.environ.get("CKPT_CHAR", "sd_xl_base_1.0.safetensors")
CONTROLNET = "controlnet-union-sdxl-1.0.safetensors"
LORA = "sdxl_lightning_4step_lora.safetensors"
IPADAPTER = "ip-adapter-plus_sdxl_vit-h.safetensors"
CLIP_VISION = "CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors"
STEPS, CFG = 8, 2.0

# ---------------------------------------------------------------- 风格提示词
STYLE = ("masterpiece, best quality, 1920s England country manor interior, "
         "visual novel background art, art deco mood, muted teal and amber palette, "
         "painterly oil illustration, soft candle light, cinematic composition, "
         "atmospheric depth, film grain, highly detailed, no people")
STYLE_NEG = ("text, watermark, signature, letters, people, humans, modern objects, car, "
             "neon, plastic, lowres, blurry, jpeg artifacts, deformed, extra limbs, cartoon")
CHAR_STYLE = ("masterpiece, best quality, visual novel character sprite, full body, standing, "
              "facing viewer, 1920s English period costume, clean lineart, soft cel shading, "
              "muted elegant colors, symmetrical, plain flat solid magenta background")
CHAR_NEG = ("text, watermark, signature, multiple views, cropped head, extra limbs, extra fingers, "
            "deformed face, modern clothes, neon, lowres, blurry, busy background, props")

# ---------------------------------------------------------------- 构图规格（blockout）
# 每一项: (x, y, w, h, 深度值 0~255)，坐标是 0~1 归一化，y 从上往下
def rect(x, y, w, h, v):
    return (x, y, w, h, v)

BG_SPECS = {
    "styles_manor_dusk": dict(
        prompt="exterior view of an English country manor house at dusk, gravel drive, tall elm trees, warm windows lit, storm clouds",
        prims=[rect(0, 0, 1, .55, 40), rect(0, .55, 1, .45, 120), rect(.15, .28, .5, .3, 200),
               rect(.30, .18, .2, .12, 230), rect(.62, .3, .12, .3, 215), rect(0, .5, 1, .06, 150)]),
    "styles_manor_night": dict(
        prompt="exterior of an English manor at night, moonlight, mist over the lawn, a few windows glowing, deep blue hour",
        prims=[rect(0, 0, 1, .6, 25), rect(0, .6, 1, .4, 90), rect(.15, .3, .5, .32, 180),
               rect(.30, .2, .2, .12, 210), rect(0, .55, 1, .2, 70)]),
    "styles_hall": dict(
        prompt="grand entrance hall of a 1920s manor, wooden staircase on the left, grandfather clock, deco rug on parquet floor, chandelier, tall window at the back",
        prims=[rect(0, 0, 1, .55, 180), rect(0, .55, 1, .45, 90), rect(.05, .3, .3, .3, 220),
               rect(.42, .2, .2, .35, 120), rect(.75, .28, .1, .3, 200), rect(0, .78, 1, .22, 60)]),
    "styles_library": dict(
        prompt="manor library at night, floor to ceiling bookshelves, lit fireplace, two leather armchairs, round table with oil lamp, dark wood paneling",
        prims=[rect(0, 0, .45, .7, 200), rect(.55, .1, .3, .5, 170), rect(.5, .5, .25, .25, 60),
               rect(.05, .65, .3, .25, 110), rect(.65, .62, .3, .25, 110), rect(0, .82, 1, .18, 70)]),
    "styles_boudoir": dict(
        prompt="elegant 1920s lady's boudoir, sofa, writing desk with papers, flowers in vase, tall window with lace, tea tray, warm afternoon light",
        prims=[rect(0, 0, 1, .62, 190), rect(0, .62, 1, .38, 100), rect(.12, .3, .22, .3, 60),
               rect(.7, .15, .25, .4, 240), rect(.4, .45, .22, .2, 150), rect(0, .8, 1, .2, 80)]),
    "styles_bedroom": dict(
        prompt="victorian manor bedroom as a crime scene, large bed with arched headboard, overturned bedside table, small fireplace with ash, mantelpiece with ornaments and a spill vase, chest of drawers with a spirit lamp, connecting door on the right, one candle",
        prims=[rect(0, 0, 1, .6, 170), rect(0, .6, 1, .4, 90), rect(.35, .45, .45, .25, 230),
               rect(.55, .1, .22, .1, 150), rect(.82, .15, .15, .5, 210), rect(.03, .3, .18, .3, 120),
               rect(.2, .55, .15, .2, 190), rect(0, .8, 1, .2, 70)]),
    "styles_corridor": dict(
        prompt="long dark corridor in a manor at night, gallery of doors, runner carpet, gas lamp, moonlight from the end window",
        prims=[rect(0, 0, .35, 1, 150), rect(.65, 0, .35, 1, 150), rect(.35, .15, .3, .6, 40),
               rect(.42, .25, .16, .4, 90), rect(.3, .8, .4, .2, 60)]),
    "styles_garden": dict(
        prompt="english manor rose garden in summer, stone bench, trellis arch, gravel path, manor house in the distance, soft daylight",
        prims=[rect(0, 0, 1, .35, 120), rect(0, .35, 1, .65, 60), rect(.1, .2, .3, .2, 190),
               rect(.55, .5, .3, .15, 100), rect(.45, .4, .12, .22, 140), rect(0, .7, 1, .3, 80)]),
    "styles_village": dict(
        prompt="english village street in 1917, half timbered shop fronts, chemist shop sign, lamp post, church tower in the distance, cobbled road",
        prims=[rect(0, 0, 1, .4, 130), rect(0, .4, 1, .6, 70), rect(.03, .3, .25, .35, 200),
               rect(.35, .32, .25, .33, 190), rect(.7, .3, .25, .35, 195), rect(0, .78, 1, .22, 55)]),
    "styles_chemist": dict(
        prompt="interior of a 1920s village chemist shop, wall of apothecary bottles, wooden counter with a poison register book, brass scales, glass jars, warm lamp light",
        prims=[rect(0, 0, 1, .6, 190), rect(0, .6, 1, .4, 110), rect(.05, .12, .9, .4, 150),
               rect(.1, .55, .8, .12, 210), rect(.35, .38, .2, .15, 240), rect(.75, .45, .15, .2, 230)]),
    "styles_cottage": dict(
        prompt="small cosy cottage sitting room of a belgian detective, fireplace, armchair, round tea table with porcelain cups, bookcase, brass ornaments, warm light",
        prims=[rect(0, 0, 1, .62, 180), rect(0, .62, 1, .38, 95), rect(.3, .3, .3, .35, 60),
               rect(.1, .3, .22, .3, 200), rect(.68, .45, .28, .2, 150), rect(0, .82, 1, .18, 75)]),
    "styles_courtroom": dict(
        prompt="edwardian courtroom interior, raised judge bench, witness box, wooden railing, rows of seats, tall arched windows with light shafts, sombre tones",
        prims=[rect(0, 0, 1, .6, 190), rect(0, .6, 1, .4, 100), rect(.55, .08, .35, .4, 240),
               rect(.12, .3, .35, .25, 215), rect(.5, .5, .2, .2, 200), rect(0, .8, 1, .2, 70)]),
    "styles_board": dict(
        prompt="cork investigation board covered with pinned notes photographs and red string, dim lamplight, evidence laid out on a table below",
        prims=[rect(0, 0, 1, .75, 150), rect(0, .75, 1, .25, 90), rect(.1, .12, .2, .2, 200),
               rect(.4, .1, .22, .22, 210), rect(.7, .14, .2, .2, 205), rect(.25, .42, .5, .2, 180)]),
}

CHAR_SPECS = {
    "poirot": "small elderly belgian detective, egg shaped head, enormous black waxed moustache, neat dark three piece suit, bow tie, gloved hands, dignified",
    "hastings": "young british army captain in his thirties, square jaw, short brown hair, khaki military tunic, honest open expression",
    "emily": "wealthy english lady in her late sixties, silver hair in a tight bun, black mourning dress with lace high collar, stern piercing gaze",
    "alfred": "gaunt englishman in his forties, black full beard, pince nez glasses, black suit, dark shadowed eyes, secretive",
    "john": "handsome english gentleman in his thirties, dark slicked hair, khaki officer uniform, proud weary face",
    "lawrence": "thin pale englishman in his late twenties, round spectacles, brown hair, grey three piece suit, melancholy expression",
    "mary": "tall elegant english lady, dark wavy centre parted hair, white wartime land smock with a green armlet, cold reserved beauty",
    "cynthia": "cheerful young english woman of twenty, auburn bobbed hair, white nurse apron over a blue dress, white cap",
    "evelyn": "severe englishwoman of fifty, short dark hair, masculine tailored black suit and tie, low commanding voice, deep set eyes",
    "dorcas": "plump english housekeeper of fifty five, grey hair under a white cap, black dress with white apron, kind worried face",
    "japp": "round faced scotland yard inspector, short brown hair, grey moustache, bowler hat, brown overcoat, practical",
    "wilkins": "elderly english country doctor, white hair, grey moustache, round spectacles, dark wool suit, kindly",
    "bauerstein": "sinister polish physician, black slicked hair, pointed goatee, monocle, dark high collar coat, cold intellect",
}

# ---------------------------------------------------------------- ComfyUI 通信
def api(path, data=None):
    url = COMFY + path
    body = json.dumps(data).encode("utf-8") if data is not None else None
    req = urllib.request.Request(url, data=body, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=120) as r:
        return json.loads(r.read().decode("utf-8"))

def queue(graph):
    res = api("/prompt", {"prompt": graph, "client_id": "styles-pipeline"})
    return res["prompt_id"]

def wait(prompt_id, timeout=900):
    t0 = time.time()
    while time.time() - t0 < timeout:
        h = api("/history/" + prompt_id)
        if prompt_id in h:
            entry = h[prompt_id]
            if entry.get("status", {}).get("completed") or entry.get("outputs"):
                return entry
        time.sleep(2)
    raise TimeoutError("ComfyUI 生成超时")

def fetch(image_info, dst):
    q = urllib.parse.urlencode({"filename": image_info["filename"],
                                "subfolder": image_info.get("subfolder", ""),
                                "type": image_info.get("type", "output")})
    with urllib.request.urlopen(COMFY + "/view?" + q, timeout=120) as r:
        data = r.read()
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    with open(dst, "wb") as f:
        f.write(data)
    return len(data)

# ---------------------------------------------------------------- 图结构
def graph_background(prompt, guide_name, ckpt, seed, w=1536, h=864, strength=0.85):
    g = {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": ckpt}},
        "2": {"class_type": "LoraLoader", "inputs": {"model": ["1", 0], "clip": ["1", 1],
              "lora_name": LORA, "strength_model": 0.9, "strength_clip": 0.9}},
        "3": {"class_type": "CLIPTextEncode", "inputs": {"text": prompt + ", " + STYLE, "clip": ["2", 1]}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"text": STYLE_NEG, "clip": ["2", 1]}},
        "5": {"class_type": "LoadImage", "inputs": {"image": guide_name}},
        "6": {"class_type": "ControlNetLoader", "inputs": {"control_net_name": CONTROLNET}},
        "7": {"class_type": "SetUnionControlNetType", "inputs": {"control_net": ["6", 0], "type": "depth"}},
        "8": {"class_type": "ControlNetApplyAdvanced", "inputs": {"positive": ["3", 0], "negative": ["4", 0],
              "control_net": ["7", 0], "image": ["5", 0], "strength": strength,
              "start_percent": 0.0, "end_percent": 0.85}},
        "9": {"class_type": "EmptyLatentImage", "inputs": {"width": w, "height": h, "batch_size": 1}},
        "10": {"class_type": "KSampler", "inputs": {"model": ["2", 0], "seed": seed, "steps": STEPS, "cfg": CFG,
               "sampler_name": "dpmpp_sde", "scheduler": "karras", "positive": ["8", 0], "negative": ["8", 1],
               "latent_image": ["9", 0], "denoise": 1.0}},
        "11": {"class_type": "VAEDecode", "inputs": {"samples": ["10", 0], "vae": ["1", 2]}},
        "12": {"class_type": "SaveImage", "inputs": {"images": ["11", 0], "filename_prefix": "styles_bg"}},
    }
    return g

def graph_character(prompt, ckpt, seed, ref_image=None, w=832, h=1216):
    g = {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": ckpt}},
        "2": {"class_type": "LoraLoader", "inputs": {"model": ["1", 0], "clip": ["1", 1],
              "lora_name": LORA, "strength_model": 0.9, "strength_clip": 0.9}},
        "3": {"class_type": "CLIPTextEncode", "inputs": {"text": prompt + ", " + CHAR_STYLE, "clip": ["2", 1]}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"text": CHAR_NEG, "clip": ["2", 1]}},
    }
    model_ref = ["2", 0]
    if ref_image:
        g["5"] = {"class_type": "LoadImage", "inputs": {"image": ref_image}}
        g["6"] = {"class_type": "IPAdapterUnifiedLoader", "inputs": {"model": ["2", 0], "preset": "PLUS (high strength)"}}
        g["7"] = {"class_type": "IPAdapterAdvanced", "inputs": {
            "model": ["6", 0], "ipadapter": ["6", 1], "image": ["5", 0], "weight": 0.72,
            "weight_type": "linear", "combine_embeds": "concat", "start_at": 0.0, "end_at": 0.85,
            "embeds_scaling": "V only"}}
        model_ref = ["7", 0]
    g["9"] = {"class_type": "EmptyLatentImage", "inputs": {"width": w, "height": h, "batch_size": 1}}
    g["10"] = {"class_type": "KSampler", "inputs": {"model": model_ref, "seed": seed, "steps": STEPS, "cfg": CFG,
               "sampler_name": "dpmpp_sde", "scheduler": "karras", "positive": ["3", 0], "negative": ["4", 0],
               "latent_image": ["9", 0], "denoise": 1.0}}
    g["11"] = {"class_type": "VAEDecode", "inputs": {"samples": ["10", 0], "vae": ["1", 2]}}
    g["12"] = {"class_type": "SaveImage", "inputs": {"images": ["11", 0], "filename_prefix": "styles_char"}}
    return g

# ---------------------------------------------------------------- 引导图
def build_guide(spec, name, w=1536, h=864):
    """用 Pillow 画一张灰度 blockout 作为 ControlNet(depth) 引导图。不需要手绘。"""
    from PIL import Image, ImageDraw, ImageFilter
    img = Image.new("L", (w, h), 128)
    d = ImageDraw.Draw(img)
    for (x, y, rw, rh, v) in spec["prims"]:
        d.rectangle([x * w, y * h, (x + rw) * w, (y + rh) * h], fill=int(v))
    img = img.filter(ImageFilter.GaussianBlur(6))
    os.makedirs(OUT_GUIDE, exist_ok=True)
    p = os.path.join(OUT_GUIDE, name + ".png")
    img.save(p)
    return p

def upload(path):
    """把本地图片上传到 ComfyUI 的 input 目录（通过 /upload/image）。"""
    boundary = "----styles" + str(random.randint(1, 1 << 30))
    with open(path, "rb") as f:
        content = f.read()
    name = os.path.basename(path)
    body = b""
    body += ("--" + boundary + "\r\n").encode()
    body += ('Content-Disposition: form-data; name="image"; filename="' + name + '"\r\n').encode()
    body += b"Content-Type: image/png\r\n\r\n" + content + b"\r\n"
    body += ("--" + boundary + "\r\n").encode()
    body += b'Content-Disposition: form-data; name="overwrite"\r\n\r\ntrue\r\n'
    body += ("--" + boundary + "--\r\n").encode()
    req = urllib.request.Request(COMFY + "/upload/image", data=body,
                                 headers={"Content-Type": "multipart/form-data; boundary=" + boundary})
    with urllib.request.urlopen(req, timeout=120) as r:
        return json.loads(r.read().decode("utf-8"))["name"]

# ---------------------------------------------------------------- 后处理
def cutout(src, dst, tol=42):
    """把品红/纯色背景抠成透明，并裁掉多余透明边。角色立绘用。"""
    from PIL import Image
    im = Image.open(src).convert("RGBA")
    px = im.load()
    w, h = im.size
    # 取四角平均色作为背景色
    corners = [px[2, 2], px[w - 3, 2], px[2, h - 3], px[w - 3, h - 3]]
    bg = tuple(sum(c[i] for c in corners) // 4 for i in range(3))
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if abs(r - bg[0]) + abs(g - bg[1]) + abs(b - bg[2]) < tol * 3:
                px[x, y] = (r, g, b, 0)
    bbox = im.getbbox()
    if bbox:
        im = im.crop(bbox)
    im.save(dst)
    return im.size

def qa(path):
    from PIL import Image
    import statistics
    im = Image.open(path)
    stat = {"file": os.path.basename(path), "size": im.size, "mode": im.mode}
    small = im.convert("L").resize((64, 64))
    vals = list(small.getdata())
    stat["mean"] = round(statistics.mean(vals), 1)
    stat["stdev"] = round(statistics.pstdev(vals), 1)
    stat["ok"] = stat["stdev"] > 8
    return stat

# ---------------------------------------------------------------- 主流程
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--kind", choices=["bg", "char"], required=True)
    ap.add_argument("--only", nargs="*", default=None)
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--seed", type=int, default=19170718)
    ap.add_argument("--ref", default="", help="角色参考图（IPAdapter），可为 ComfyUI input 名或本地路径")
    args = ap.parse_args()

    report = []
    targets = []
    if args.kind == "bg":
        targets = list(BG_SPECS.keys())
    else:
        targets = list(CHAR_SPECS.keys())
    if args.only:
        targets = [t for t in targets if t in args.only]
    if args.limit:
        targets = targets[:args.limit]

    ref_name = None
    if args.ref:
        ref_name = os.path.basename(args.ref) if not os.path.exists(args.ref) else upload(args.ref)

    for i, name in enumerate(targets):
        seed = args.seed + i * 977
        try:
            if args.kind == "bg":
                spec = BG_SPECS[name]
                guide = build_guide(spec, name)
                gname = upload(guide)
                ck = CKPT_BG
                g = graph_background(spec["prompt"], gname, ck, seed)
                dst = os.path.join(ART, "Backgrounds", name + ".png")
            else:
                ck = CKPT_CHAR
                g = graph_character(CHAR_SPECS[name], ck, seed, ref_name)
                dst = os.path.join(ART, "Characters", name + "_neutral.png")
            pid = queue(g)
            entry = wait(pid)
            outs = entry.get("outputs", {})
            imgs = []
            for node in outs.values():
                imgs += node.get("images", [])
            if not imgs:
                raise RuntimeError("没有返回图像")
            tmp = dst + ".raw.png"
            n = fetch(imgs[0], tmp)
            if args.kind == "char":
                size = cutout(tmp, dst)
                os.remove(tmp)
            else:
                os.replace(tmp, dst)
            st = qa(dst)
            st["name"] = name
            report.append(st)
            print("[OK] %-26s %s" % (name, st))
        except Exception as e:
            print("[FAIL] %-24s %s" % (name, e))
            report.append({"name": name, "error": str(e), "ok": False})

    os.makedirs(os.path.dirname(OUT_MANIFEST), exist_ok=True)
    rp = os.path.join(ROOT, "ToolsOut", "qa_report.json")
    old = []
    if os.path.exists(rp):
        try: old = json.load(open(rp, encoding="utf-8"))
        except Exception: old = []
    json.dump(old + report, open(rp, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    print("质检报告: " + rp)

if __name__ == "__main__":
    main()
