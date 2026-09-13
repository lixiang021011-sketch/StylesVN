#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
《斯泰尔斯庄园奇案》美术生产线 v2
ComfyUI + ControlNet（Union SDXL）+ Lightning LoRA（8 步出图）；立绘不再使用背景图做 IPAdapter 参考。

流程：
    ToolsOut/asset_manifest.json  →  ［本脚本］  →  Assets/Resources/Art/{Backgrounds,Characters}/*.png
                          ▲                                    │
                   blockout 构图规格 ──► ControlNet 引导图 ──┘

v1 的两个致命问题（本次修复）：
  1. 立绘用「背景图」当 IPAdapter 参考，导致每张立绘都被注入同一个书架场景 → 13 张立绘全是房间照。
  2. 抠图用「与四角平均色的全局色差」，把人物身上的米色/暖色一并抠掉 → 画面碎裂。

用法：
    python tools/comfy/pipeline.py --kind bg --only styles_bedroom styles_library
    python tools/comfy/pipeline.py --kind char --only poirot
    python tools/comfy/pipeline.py --kind char --all
    python tools/comfy/pipeline.py --kind bg --all
    python tools/comfy/pipeline.py --kind title
"""
import argparse
import json
import os
import random
import sys
import time
import urllib.parse
import urllib.request

COMFY = os.environ.get("COMFY_URL", "http://127.0.0.1:8188")
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
# 必须放在 Resources 下：运行时代码用 Resources.Load<Sprite>("Art/...") 读取，否则打包不会收录
ART = os.path.join(ROOT, "Assets", "Resources", "Art")
OUT_GUIDE = os.path.join(ROOT, "ToolsOut", "control")
OUT_MANIFEST = os.path.join(ROOT, "ToolsOut", "asset_manifest.json")
OUT_RAW = os.path.join(ROOT, "ToolsOut", "raw")

CKPT_BG = os.environ.get("CKPT_BG", "sd_xl_base_1.0.safetensors")
CKPT_CHAR = os.environ.get("CKPT_CHAR", "sd_xl_base_1.0.safetensors")
CONTROLNET = "controlnet-union-sdxl-1.0.safetensors"
LORA = "sdxl_lightning_4step_lora.safetensors"
IPADAPTER = "ip-adapter-plus_sdxl_vit-h.safetensors"

STEPS, CFG = 8, 2.0

# 立绘成品画布；与游戏里 RectTransform 的 337x561 比例一致（0.6842）
CHAR_W, CHAR_H = 832, 1216
BG_W, BG_H = 1536, 864
TITLE_W, TITLE_H = 1920, 1080

# ---------------------------------------------------------------- 风格提示词
STYLE = ("1920s England country manor interior, visual novel background art, art deco mood, "
         "muted teal and amber palette, painterly oil illustration, soft candle light, "
         "cinematic composition, atmospheric depth, subtle film grain, highly detailed, "
         "empty room, no people")
STYLE_NEG = ("text, watermark, signature, letters, numbers, people, humans, portraits, "
             "modern objects, car, neon, plastic, lowres, blurry, jpeg artifacts, deformed, "
             "cartoon, anime, flat vector, cluttered, oversaturated, fisheye")

CHAR_STYLE = ("single character, full body visual novel character sprite, standing, "
              "head to toe fully inside the frame, facing the viewer, 1920s English period costume, "
              "painterly oil illustration, semi realistic proportions, soft brushwork, "
              "muted teal amber and sepia palette, subtle film grain, restrained elegant mood, "
              "even soft studio lighting, crisp silhouette, no props")
CHAR_NEG = ("text, watermark, signature, letters, multiple people, two characters, extra person, "
            "cropped head, cropped feet, cut off at knees, close up, portrait crop, extra limbs, "
            "extra fingers, deformed face, deformed hands, modern clothes, neon, lowres, blurry, "
            "busy background, room, interior, furniture, bookshelf, wall, window, scenery, "
            "landscape, props, weapon, cartoon, chibi, vector art, flat colors, cel shading, "
            "paper doll, gradient background, floor, ground plane, horizon line, "
            "painted backdrop, shadow on the ground, cast shadow, drop shadow, "
            "dark shadow behind the subject, colour band")

TITLE_STYLE = ("key art for a 1920s english murder mystery visual novel, art deco poster, "
               "grand english manor house at dusk seen through a rain streaked window, "
               "silhouette of a small elegant detective in a bowler hat in the foreground, "
               "muted teal and amber palette, painterly oil illustration, cinematic lighting, "
               "atmospheric fog, dramatic composition, empty space in the middle for a title, "
               "no text, no letters")
TITLE_NEG = ("text, watermark, signature, letters, numbers, typography, logo, people in the "
             "middle, crowd, modern objects, neon, lowres, blurry, jpeg artifacts, deformed")


# ---------------------------------------------------------------- 构图规格（blockout）
# 每一项: (x, y, w, h, 深度值 0~255)，坐标是 0~1 归一化，y 从上往下
def rect(x, y, w, h, v):
    return (x, y, w, h, v)


def band(y, h, v, x=0.0, w=1.0):
    """整条横向色块：用来铺墙/地板。"""
    return rect(x, y, w, h, v)


BG_SPECS = {
    # --- 室外 ---
    "styles_manor_dusk": dict(
        prompt="exterior of a large english country manor house at dusk, gravel drive leading to the door, "
               "tall elm trees, warm lit windows, storm clouds, distant lawn, wrought iron gate",
        prims=[band(0, .52, 30), band(.52, .48, 150),
               rect(.26, .20, .44, .30, 235), rect(.42, .14, .12, .08, 245),
               rect(.08, .30, .10, .22, 190), rect(.82, .28, .10, .24, 190),
               rect(.44, .44, .08, .10, 120), rect(0, .62, 1, .10, 95)]),
    "styles_manor_night": dict(
        prompt="exterior of an english manor at night under a full moon, mist drifting over the lawn, "
               "one window glowing amber, bare trees, deep blue hour, cold moonlight",
        prims=[band(0, .58, 20), band(.58, .42, 95),
               rect(.26, .26, .44, .30, 200), rect(.42, .18, .12, .08, 215),
               rect(.10, .36, .09, .20, 150), rect(.82, .34, .09, .22, 150),
               band(.52, .18, 65)]),
    "styles_garden": dict(
        prompt="english manor rose garden in high summer, stone bench, brick wall covered with climbing roses, "
               "gravel path, trellis arch, lawn, soft afternoon daylight",
        prims=[band(0, .34, 210), band(.34, .30, 110), band(.64, .36, 60),
               rect(.04, .16, .26, .22, 235), rect(.70, .14, .26, .24, 235),
               rect(.40, .26, .20, .30, 170), rect(.44, .34, .12, .18, 90),
               rect(.18, .50, .22, .12, 140), rect(.62, .48, .20, .13, 145)]),
    "styles_village": dict(
        prompt="english village high street in 1917, half timbered shop fronts with hanging signs, "
               "a chemist shop with big glass windows, gas lamp post, church tower behind the roofs, "
               "cobbled road, overcast daylight",
        prims=[band(0, .38, 200), band(.38, .30, 150), band(.68, .32, 70),
               rect(.02, .22, .26, .40, 230), rect(.34, .18, .28, .44, 225),
               rect(.68, .24, .30, .38, 230),
               rect(.12, .30, .12, .12, 120), rect(.44, .28, .12, .12, 120),
               rect(.76, .32, .14, .12, 120),
               rect(.47, .06, .06, .16, 175), rect(.36, .30, .05, .30, 205)]),
    # --- 室内 ---
    "styles_hall": dict(
        prompt="grand entrance hall of a 1920s manor, wide wooden staircase rising on the left, "
               "long case grandfather clock, persian rug on parquet floor, brass chandelier, "
               "tall arched window above the half landing, panelled walls",
        prims=[band(0, .56, 175), band(.56, .44, 95),
               rect(.02, .34, .34, .30, 225), rect(.10, .48, .24, .18, 150),
               rect(.40, .22, .18, .36, 115), rect(.68, .30, .08, .30, 210),
               rect(.80, .34, .16, .26, 195), rect(.30, .68, .42, .14, 130),
               band(.80, .20, 70)]),
    "styles_library": dict(
        prompt="manor library at night, floor to ceiling mahogany bookshelves, lit stone fireplace, "
               "two worn leather armchairs, round side table with an oil lamp, dark wood panelling, "
               "deep shadow, warm pool of light",
        prims=[band(0, .68, 195), band(.68, .32, 90),
               rect(.0, .04, .40, .62, 215), rect(.56, .06, .30, .40, 200),
               rect(.44, .40, .26, .22, 105), rect(.62, .52, .28, .20, 130),
               rect(.06, .54, .30, .20, 130), rect(.62, .56, .22, .16, 60),
               band(.84, .16, 70)]),
    "styles_boudoir": dict(
        prompt="elegant 1920s lady's boudoir, chaise longue with silk cushions, writing desk with "
               "scattered papers and a fountain pen, vase of roses, tall sash window with lace "
               "curtains, silver tea tray, warm afternoon light, patterned wallpaper",
        prims=[band(0, .60, 185), band(.60, .40, 105),
               rect(.06, .34, .24, .30, 115), rect(.66, .12, .28, .46, 245),
               rect(.34, .48, .26, .16, 155), rect(.10, .60, .20, .12, 130),
               band(.78, .22, 85)]),
    "styles_bedroom": dict(
        prompt="victorian manor bedroom as a crime scene, large bed with a tall arched headboard, "
               "overturned bedside table, small marble fireplace with grey ash, mantelpiece with "
               "porcelain ornaments and a spill vase, chest of drawers with a spirit lamp, "
               "connecting door on the right, a single candle, cold morning light",
        prims=[band(0, .58, 165), band(.58, .42, 90),
               rect(.30, .34, .38, .30, 235), rect(.34, .22, .30, .14, 200),
               rect(.74, .16, .16, .48, 220), rect(.02, .30, .16, .28, 130),
               rect(.16, .56, .12, .18, 195), rect(.50, .60, .10, .10, 205),
               band(.80, .20, 70)]),
    "styles_corridor": dict(
        prompt="long dim corridor inside a manor at night, row of panelled doors on both sides, "
               "worn red runner carpet, single gas lamp on the wall, cold moonlight falling from "
               "a window at the far end, deep perspective",
        prims=[rect(0, 0, .34, 1, 160), rect(.66, 0, .34, 1, 160),
               rect(.34, .12, .32, .62, 45), rect(.40, .26, .20, .40, 85),
               rect(.68, .30, .08, .34, 190), rect(.24, .30, .08, .34, 190),
               rect(.30, .80, .40, .20, 60)]),
    "styles_cottage": dict(
        prompt="small cosy sitting room of a belgian detective, brick fireplace with a low fire, "
               "wing back armchair, round tea table with a porcelain cup, tall bookcase, "
               "brass ornaments on the mantel, lace antimacassars, warm lamplight",
        prims=[band(0, .62, 175), band(.62, .38, 95),
               rect(.26, .34, .28, .32, 70), rect(.02, .26, .20, .30, 205),
               rect(.70, .40, .28, .22, 145), rect(.34, .58, .18, .14, 150),
               rect(.56, .30, .12, .18, 185), band(.82, .18, 75)]),
    "styles_courtroom": dict(
        prompt="edwardian courtroom interior, raised judge bench with a carved canopy, witness box, "
               "wooden railing and dock, rows of benches for the public, tall arched windows "
               "throwing pale light shafts, sombre brown and teal tones",
        prims=[band(0, .60, 190), band(.60, .40, 100),
               rect(.54, .06, .38, .42, 245), rect(.08, .30, .34, .26, 220),
               rect(.44, .46, .20, .18, 205), rect(.02, .56, .96, .08, 180),
               rect(.10, .66, .80, .10, 120), band(.82, .18, 70)]),
    "styles_chemist": dict(
        prompt="interior of a 1920s village chemist shop, tall shelves packed with apothecary "
               "bottles and glass jars, dark wooden counter, brass scales, an open poison "
               "register book on the counter, leaded shop window with daylight, dust motes",
        prims=[band(0, .58, 185), band(.58, .42, 100),
               rect(.02, .06, .96, .44, 200), rect(.06, .50, .88, .10, 215),
               rect(.30, .36, .18, .14, 240), rect(.72, .42, .14, .16, 235),
               rect(.08, .14, .84, .04, 150), band(.80, .20, 75)]),
    "styles_board": dict(
        prompt="cork investigation board on a dim wall covered with pinned notes, photographs, "
               "newspaper cuttings and red string linking them, a few photographs turned face down, "
               "oil lamp light raking across the surface, evidence laid out on a table below",
        prims=[rect(.06, .06, .88, .56, 200), rect(.0, .62, 1, .38, 95),
               rect(.10, .12, .18, .18, 235), rect(.36, .10, .20, .20, 240),
               rect(.64, .14, .18, .18, 235), rect(.22, .38, .48, .18, 220),
               rect(.10, .68, .80, .16, 150), band(.86, .14, 70)]),
}

CHAR_SPECS = {
    "poirot": "a small elderly belgian detective, egg shaped bald head, enormous black waxed "
              "moustache, neat dark grey three piece suit, bow tie, gloves, hands clasped, "
              "dignified upright posture",
    "hastings": "a young british army captain in his early thirties, square jaw, short brown hair, "
                "khaki military tunic with leather belt and Sam Browne straps, honest open face",
    "emily": "a wealthy english lady in her late sixties, silver hair drawn into a tight bun, "
             "black mourning dress with a lace high collar and jet brooch, stern piercing gaze, "
             "straight backed",
    "alfred": "a gaunt englishman in his mid forties, full black beard, pince nez glasses on a "
              "cord, black suit and high collar, hollow shadowed eyes, secretive expression",
    "john": "a handsome english gentleman in his thirties, dark hair slicked back, khaki officer "
            "tunic with captain's pips, proud but weary face, upright stance",
    "lawrence": "a thin pale englishman in his late twenties, round wire spectacles, light brown "
                "hair, soft grey three piece suit, melancholy hesitant expression",
    "mary": "a tall elegant english lady of thirty, dark wavy centre parted hair, white wartime "
            "land smock over a dark skirt with a green armlet, cold reserved beauty, gloved hands",
    "cynthia": "a cheerful young english woman of twenty, auburn bobbed hair, white nurse apron "
               "over a blue dress, small white cap, bright friendly face",
    "evelyn": "a severe englishwoman of fifty, short dark hair, masculine tailored black walking "
              "suit with a waistcoat and tie, deep set commanding eyes, hands behind her back",
    "dorcas": "a plump english housekeeper of fifty five, grey hair under a white mob cap, black "
              "dress with a crisp white apron and folded sleeves, kind worried face",
    "japp": "a round faced scotland yard inspector of forty five, short brown hair, grey moustache, "
            "bowler hat in one hand, brown overcoat of heavy wool, practical boots",
    "wilkins": "an elderly english country doctor in his seventies, white hair, grey moustache, "
               "round gold spectacles, dark wool suit and watch chain, kindly tired face",
    "bauerstein": "a sinister polish physician of fifty, black slicked hair, pointed goatee, "
                  "monocle, dark high collar frock coat, thin cold intellectual face",
}

# 每个角色选一把「最不容易和自身配色打架」的键色：
#   衣服是青绿/墨绿 → 不能用绿幕；脸上血色重或戴紫红领结 → 不能用品红；
#   其余人物一律用品红（反差最大，边缘最干净）。
CHAR_KEYS = {
    # 目前全部用品红：其余键色在这套画风里都会被提示词带跑（模型照样画出米黄/青绿背景）

}
DEFAULT_KEY = "magenta"


TITLE_SPEC = dict(
    prompt="grand english manor house at dusk behind rain streaked glass, gravel drive, distant "
           "lit windows, bare elm trees, a small silhouette of an elegant detective in a bowler "
           "hat and long coat standing at the lower left, thick atmospheric fog, deep teal sky "
           "with amber light breaking through")


# ---------------------------------------------------------------- ComfyUI 通信
def api(path, data=None, timeout=180):
    url = COMFY + path
    body = json.dumps(data).encode("utf-8") if data is not None else None
    req = urllib.request.Request(url, data=body, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.loads(r.read().decode("utf-8"))


def queue(graph):
    return api("/prompt", {"prompt": graph, "client_id": "styles-pipeline"})["prompt_id"]


def wait(prompt_id, timeout=1800):
    t0 = time.time()
    last = ""
    while time.time() - t0 < timeout:
        h = api("/history/" + prompt_id)
        if prompt_id in h:
            entry = h[prompt_id]
            status = entry.get("status", {})
            if status.get("status_str") == "error":
                msgs = []
                for m in status.get("messages", []):
                    msgs.append(str(m))
                raise RuntimeError("ComfyUI 执行报错: " + " | ".join(msgs)[:500])
            if entry.get("outputs"):
                return entry
        last = prompt_id
        time.sleep(2)
    raise TimeoutError("ComfyUI 生成超时 " + last)


def fetch(image_info, dst):
    q = urllib.parse.urlencode({"filename": image_info["filename"],
                                "subfolder": image_info.get("subfolder", ""),
                                "type": image_info.get("type", "output")})
    with urllib.request.urlopen(COMFY + "/view?" + q, timeout=180) as r:
        data = r.read()
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    with open(dst, "wb") as f:
        f.write(data)
    return len(data)


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
    with urllib.request.urlopen(req, timeout=180) as r:
        return json.loads(r.read().decode("utf-8"))["name"]


# ---------------------------------------------------------------- 图结构
def loader_chain(ckpt):
    return {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": ckpt}},
        "2": {"class_type": "LoraLoader", "inputs": {"model": ["1", 0], "clip": ["1", 1],
              "lora_name": LORA, "strength_model": 0.9, "strength_clip": 0.9}},
    }


def graph_guided(prompt, negative, ckpt, seed, guide_name, w, h, strength, prefix,
                 steps=None, cfg=None, end_percent=0.62):
    g = loader_chain(ckpt)
    steps = STEPS if steps is None else steps
    cfg = CFG if cfg is None else cfg
    g["3"] = {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["2", 1]}}
    g["4"] = {"class_type": "CLIPTextEncode", "inputs": {"text": negative, "clip": ["2", 1]}}
    g["5"] = {"class_type": "LoadImage", "inputs": {"image": guide_name}}
    g["6"] = {"class_type": "ControlNetLoader", "inputs": {"control_net_name": CONTROLNET}}
    g["7"] = {"class_type": "SetUnionControlNetType", "inputs": {"control_net": ["6", 0], "type": "depth"}}
    g["8"] = {"class_type": "ControlNetApplyAdvanced", "inputs": {
        "positive": ["3", 0], "negative": ["4", 0], "control_net": ["7", 0], "image": ["5", 0],
        "strength": strength, "start_percent": 0.0, "end_percent": end_percent}}
    g["9"] = {"class_type": "EmptyLatentImage", "inputs": {"width": w, "height": h, "batch_size": 1}}
    g["10"] = {"class_type": "KSampler", "inputs": {
        "model": ["2", 0], "seed": seed, "steps": steps, "cfg": cfg,
        "sampler_name": "dpmpp_sde", "scheduler": "karras",
        "positive": ["8", 0], "negative": ["8", 1], "latent_image": ["9", 0], "denoise": 1.0}}
    g["11"] = {"class_type": "VAEDecode", "inputs": {"samples": ["10", 0], "vae": ["1", 2]}}
    g["12"] = {"class_type": "SaveImage", "inputs": {"images": ["11", 0], "filename_prefix": prefix}}
    return g


def graph_plain(prompt, negative, ckpt, seed, w, h, prefix, steps=None, cfg=None):
    g = loader_chain(ckpt)
    steps = STEPS if steps is None else steps
    cfg = CFG if cfg is None else cfg
    g["3"] = {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["2", 1]}}
    g["4"] = {"class_type": "CLIPTextEncode", "inputs": {"text": negative, "clip": ["2", 1]}}
    g["9"] = {"class_type": "EmptyLatentImage", "inputs": {"width": w, "height": h, "batch_size": 1}}
    g["10"] = {"class_type": "KSampler", "inputs": {
        "model": ["2", 0], "seed": seed, "steps": steps, "cfg": cfg,
        "sampler_name": "dpmpp_sde", "scheduler": "karras",
        "positive": ["3", 0], "negative": ["4", 0], "latent_image": ["9", 0], "denoise": 1.0}}
    g["11"] = {"class_type": "VAEDecode", "inputs": {"samples": ["10", 0], "vae": ["1", 2]}}
    g["12"] = {"class_type": "SaveImage", "inputs": {"images": ["11", 0], "filename_prefix": prefix}}
    return g


# ---------------------------------------------------------------- 引导图
def build_bg_guide(spec, name, w=BG_W, h=BG_H):
    """用 Pillow 画一张灰度 blockout 作为 ControlNet(depth) 引导图。不需要手绘。"""
    from PIL import Image, ImageDraw, ImageFilter
    img = Image.new("L", (w, h), 128)
    d = ImageDraw.Draw(img)
    for (x, y, rw, rh, v) in spec["prims"]:
        d.rectangle([x * w, y * h, (x + rw) * w, (y + rh) * h], fill=int(v))
    img = img.filter(ImageFilter.GaussianBlur(9))
    os.makedirs(OUT_GUIDE, exist_ok=True)
    p = os.path.join(OUT_GUIDE, name + ".png")
    img.save(p)
    return p


def build_figure_guide(name, w=CHAR_W, h=CHAR_H):
    """立绘引导图：一个居中、完整（头顶到脚底）的人形剪影，只用来约束「单人全身」的构图。"""
    from PIL import Image, ImageDraw, ImageFilter
    img = Image.new("L", (w, h), 20)          # 背景：近处（暗）
    d = ImageDraw.Draw(img)
    cx = w * 0.5
    head_r = w * 0.085
    # 头
    d.ellipse([cx - head_r, h * 0.045, cx + head_r, h * 0.045 + head_r * 2], fill=215)
    # 颈+躯干（梯形）
    d.polygon([(cx - w * 0.055, h * 0.175), (cx + w * 0.055, h * 0.175),
               (cx + w * 0.150, h * 0.26), (cx + w * 0.150, h * 0.55),
               (cx - w * 0.150, h * 0.55), (cx - w * 0.150, h * 0.26)], fill=205)
    # 腿
    d.rectangle([cx - w * 0.115, h * 0.55, cx - w * 0.020, h * 0.965], fill=195)
    d.rectangle([cx + w * 0.020, h * 0.55, cx + w * 0.115, h * 0.965], fill=195)
    # 手臂
    d.rectangle([cx - w * 0.195, h * 0.27, cx - w * 0.140, h * 0.60], fill=190)
    d.rectangle([cx + w * 0.140, h * 0.27, cx + w * 0.195, h * 0.60], fill=190)
    img = img.filter(ImageFilter.GaussianBlur(7))
    os.makedirs(OUT_GUIDE, exist_ok=True)
    p = os.path.join(OUT_GUIDE, name + ".png")
    img.save(p)
    return p


# ---------------------------------------------------------------- 后处理
def _rgb_to_hsv_arrays(a):
    """a: int16 的 (h,w,4) 数组 → 色相(0~360) / 饱和度(0~1)。"""
    import numpy as np
    r, g, b = a[..., 0].astype(np.float32), a[..., 1].astype(np.float32), a[..., 2].astype(np.float32)
    mx = np.maximum(np.maximum(r, g), b)
    mn = np.minimum(np.minimum(r, g), b)
    d = mx - mn
    sat = np.where(mx > 0, d / np.maximum(mx, 1.0), 0.0)
    d_safe = np.where(d == 0, 1.0, d)
    hue = np.zeros_like(mx)
    m = (mx == r)
    hue[m] = ((g - b) / d_safe)[m] % 6.0
    m = (mx == g)
    hue[m] = ((b - r) / d_safe)[m] + 2.0
    m = (mx == b)
    hue[m] = ((r - g) / d_safe)[m] + 4.0
    return (hue * 60.0) % 360.0, sat


def _hue_gap(h, h0):
    import numpy as np
    d = np.abs(h - h0) % 360.0
    return np.minimum(d, 360.0 - d)


def measure_bg_hue(path):
    """量一下生成的幕布色相，用来判断这次出图有没有按要求的键色来画。"""
    import numpy as np
    from PIL import Image
    a = np.asarray(Image.open(path).convert("RGBA"), dtype=np.int16)
    h, w, _ = a.shape
    border = np.concatenate([a[0], a[1], a[h - 1], a[h - 2],
                             a[:, 0], a[:, 1], a[:, w - 1], a[:, w - 2]])
    bh, bs = _rgb_to_hsv_arrays(border.reshape(-1, 1, 4))
    return float(np.median(bh)), float(np.median(bs))


KEYS = {
    # 键色方案：背景越远离人物自己的配色，抠图越干净。
    # 品红/绿幕很醒目但会给人物边缘染色（红脸 / 绿西装），白底最不容易染色。
    "magenta": dict(prompt="the background is a flat uniform vivid magenta screen, solid magenta "
                           "colour fill, absolutely no texture, no brush strokes, no gradient, "
                           "no vignette, no cast shadow, subject cut out on magenta",
                    negative="painted background, textured background, mottled background, sepia, "
                             "beige, terracotta, teal background, warm background, gradient background",
                    # lum_tol 放宽到 340：模型常在人物背后加一层「投影」，
                    # 它比幕布暗很多，卡亮度就会被当成前景留下来（人物身后拖一块脏影）。
                    # 人物本体靠色相 + 连通性保护，不靠亮度。
                    kind="hue", want=325.0, want_tol=42.0, tol=30.0, sat_ratio=0.60, lum_tol=340),
    "green": dict(prompt="isolated on a completely flat uniform chroma key green background, "
                         "solid green screen backdrop, saturated green, no gradient, no vignette",
                  negative="green background, teal background",
                  kind="hue", want=120.0, want_tol=45.0, tol=30.0, sat_ratio=0.60, lum_tol=215),
    "white": dict(prompt="isolated on a plain flat uniform pure white background, "
                         "clean seamless white studio backdrop, evenly lit, no gradient, no shadow",
                  negative="beige background, sepia background, warm light background, gradient background",
                  kind="light", want=0.0, want_tol=999.0, sat_max=0.18, val_min=0.78, lum_tol=200),
}


def cutout_alpha(src, key="magenta", sat_ratio=None, hue_tol=None, lum_tol=None, local_step=90,
                 feather=1.1, erode=1, hole_ratio=0.02):
    """
    抠图 v2：按「色度」而不是「整体色差」判背景。

    人物是低饱和的灰/黑/米色，背景是高饱和的品红幕。
    只有【色相接近背景】【饱和度不低于背景的 sat_ratio 倍】【整体亮度差不算离谱】的像素
    才算背景，再从这个种子集做四连通泛洪，只保留与画布边缘连通的部分。
    这样深色西装不会因为「跟背景的 RGB 距离也还行」被吃掉（v1 的翻车点）。
    """
    from PIL import Image, ImageFilter
    import collections
    import numpy as np

    cfg = KEYS[key]
    sat_ratio = cfg.get("sat_ratio", 0.5) if sat_ratio is None else sat_ratio
    hue_tol = cfg.get("tol", 40.0) if hue_tol is None else hue_tol
    lum_tol = cfg.get("lum_tol", 230) if lum_tol is None else lum_tol

    im = Image.open(src).convert("RGBA")
    w, h = im.size
    a = np.asarray(im, dtype=np.int16)
    hue, sat = _rgb_to_hsv_arrays(a)
    val = np.maximum(np.maximum(a[..., 0], a[..., 1]), a[..., 2]).astype(np.float32) / 255.0

    border = np.concatenate([a[0], a[1], a[h - 1], a[h - 2],
                             a[:, 0], a[:, 1], a[:, w - 1], a[:, w - 2]])
    bh, bs = _rgb_to_hsv_arrays(border.reshape(-1, 1, 4))
    h0 = float(np.median(bh))
    s0 = float(np.median(bs))

    bgr, bgg, bgb = (int(np.median(border[:, 0])), int(np.median(border[:, 1])), int(np.median(border[:, 2])))
    l1 = (np.abs(a[..., 0] - bgr) + np.abs(a[..., 1] - bgg) + np.abs(a[..., 2] - bgb))
    if cfg["kind"] == "hue":
        # 用「实测」的底边色相当键色，而不是提示词里写的理想色：
        # 模型画出来的幕布往往偏色（品红画成陶土红），按实测值抠才准。
        target = h0
        bg_like = ((sat >= max(0.10, s0 * sat_ratio)) &
                   (_hue_gap(hue, target) <= hue_tol) &
                   (l1 < lum_tol))
    else:  # light：白底靠「够亮 + 没颜色」判定
        bg_like = ((sat <= cfg["sat_max"]) & (val >= cfg["val_min"]) & (l1 < lum_tol))
    a32 = a.astype(np.int32)

    # 四连通泛洪：只吃掉与边缘连通的背景，人物内部的同类色块自动保留
    reached = np.zeros((h, w), dtype=bool)
    q = collections.deque()
    for x in range(w):
        for y in (0, h - 1):
            if bg_like[y, x] and not reached[y, x]:
                reached[y, x] = True
                q.append((x, y))
    for y in range(h):
        for x in (0, w - 1):
            if bg_like[y, x] and not reached[y, x]:
                reached[y, x] = True
                q.append((x, y))
    while q:
        x, y = q.popleft()
        cur = a32[y, x]
        for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if 0 <= nx < w and 0 <= ny < h and not reached[ny, nx] and bg_like[ny, nx]:
                # 局部跳变限制：挡住「硬边」，只允许背景自身的平滑过渡（含抗锯齿羽化带）
                step = (abs(int(a32[ny, nx, 0]) - int(cur[0])) +
                        abs(int(a32[ny, nx, 1]) - int(cur[1])) +
                        abs(int(a32[ny, nx, 2]) - int(cur[2])))
                if step <= local_step:
                    reached[ny, nx] = True
                    q.append((nx, ny))

    # 先把背景「收边 1px」，让被抗锯齿羽化带连通的针孔变成封闭区域；
    # 再把这些封闭区域里「颜色明显不是背景色」的补回来（例如被误伤的粉色脸颊），
    # 腋下/腿间那种被围住的真实背景仍然保持透明。
    tight = np.asarray(Image.fromarray(np.where(reached, 255, 0).astype(np.uint8), "L")
                       .filter(ImageFilter.MinFilter(3)), dtype=np.uint8) > 0
    opaque = ~tight
    holes = np.zeros_like(reached)
    visited = np.zeros_like(reached)
    n_opaque = max(1, int(opaque.sum()))
    ys, xs = np.nonzero(~opaque)
    for i in range(len(ys)):
        sy, sx = int(ys[i]), int(xs[i])
        if visited[sy, sx]:
            continue
        comp = []
        dq = collections.deque([(sx, sy)])
        visited[sy, sx] = True
        touch = False
        while dq:
            cx, cy = dq.popleft()
            comp.append((cx, cy))
            if cx in (0, w - 1) or cy in (0, h - 1):
                touch = True
            for nx, ny in ((cx - 1, cy), (cx + 1, cy), (cx, cy - 1), (cx, cy + 1)):
                if 0 <= nx < w and 0 <= ny < h and not visited[ny, nx] and not opaque[ny, nx]:
                    visited[ny, nx] = True
                    dq.append((nx, ny))
        if touch or len(comp) > n_opaque * hole_ratio:
            continue
        # 只有「颜色明显不是背景色」的封闭区域才补回来（例如被误伤的粉色脸颊）；
        # 腋下/腿间那种被围住的真实背景仍然保持透明。
        cxv = np.fromiter((l1[cy, cx] for cx, cy in comp), dtype=np.float32, count=len(comp))
        if float(cxv.mean()) > 110:
            for cx, cy in comp:
                holes[cy, cx] = True
    reached = reached & ~holes

    # 最终清理：只保留真正的「人物主体」。
    #   A) 与画布边缘相连、又不是最大块的 → 那是模型顺手画的色带/地板，按背景处理
    #   B) 封闭但与幕布同色的块           → 腋下、腿间那种被围住的幕布
    #   C) 明显小于主体的孤立碎点         → 幕布纹理被误判成前景的脏点
    solid = ~reached
    seen = np.zeros_like(solid)
    comps = []
    ys2, xs2 = np.nonzero(solid)
    n_total = float(w * h)
    for i in range(len(ys2)):
        sy, sx = int(ys2[i]), int(xs2[i])
        if seen[sy, sx]:
            continue
        comp = []
        dq = collections.deque([(sx, sy)])
        seen[sy, sx] = True
        touch = False
        while dq:
            cx, cy = dq.popleft()
            comp.append((cx, cy))
            if cx in (0, w - 1) or cy in (0, h - 1):
                touch = True
            for nx, ny in ((cx - 1, cy), (cx + 1, cy), (cx, cy - 1), (cx, cy + 1)):
                if 0 <= nx < w and 0 <= ny < h and not seen[ny, nx] and solid[ny, nx]:
                    seen[ny, nx] = True
                    dq.append((nx, ny))
        comps.append((comp, touch))
    biggest = max((len(c) for c, _ in comps), default=0)
    stats_speck = 0
    for comp, touch in comps:
        size = len(comp)
        if size >= biggest * 0.90:
            continue                                   # 最大块 = 人物本体
        cxv = np.fromiter((l1[cy, cx] for cx, cy in comp), dtype=np.float32, count=size)
        mean_l1 = float(cxv.mean())
        drop = (mean_l1 < 150 and size < n_total * 0.25) or \
               (touch and size < biggest * 0.5) or \
               (size < biggest * 0.10)
        if drop:
            for cx, cy in comp:
                reached[cy, cx] = True
            stats_speck += size

    mask = Image.fromarray(np.where(reached, 255, 0).astype(np.uint8), "L")
    for _ in range(erode):
        mask = mask.filter(ImageFilter.MinFilter(3))
    soft = mask.filter(ImageFilter.GaussianBlur(feather))
    im.putalpha(soft.point(lambda v: 255 - v))
    stats = {"key": key, "bgHue": round(h0, 1), "bgSat": round(s0, 3),
             "coverage": round(1.0 - float(reached.mean()), 3), "speckPixels": stats_speck}
    return im, stats


def normalize_sprite(im, w=CHAR_W, h=CHAR_H, fill=0.965, despill=True):
    """裁到人物外框，等比缩放到画布高度的 fill 倍，底部对齐、水平居中。"""
    from PIL import Image
    bbox = im.getbbox()
    if bbox:
        im = im.crop(bbox)
    cw, ch = im.size
    target_h = h * fill
    scale = min(target_h / ch, (w * 0.98) / cw)
    nw, nh = max(1, int(round(cw * scale))), max(1, int(round(ch * scale)))
    im = im.resize((nw, nh), Image.LANCZOS)
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    canvas.paste(im, ((w - nw) // 2, h - nh - int(h * 0.015)), im)
    return canvas


def despill(im, key="magenta", amount=0.6):
    """去掉幕布色在人物边缘留下的轮廓光（绿幕去绿边、品红幕去红边）。"""
    if key == "white":
        return im
    px = im.load()
    w, h = im.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            if key == "green":
                if g > r and g > b:
                    over = g - max(r, b)
                    if over > 10:
                        px[x, y] = (r, int(g - over * amount), b, a)
            else:  # magenta
                if r > g and b > g:
                    over = min(r, b) - g
                    if over > 10:
                        px[x, y] = (int(r - over * amount), g, int(b - over * amount), a)
    return im


def bottom_band_ratio(im, rows=0.12, thresh=200):
    """画面最底部那几行里不透明像素的横向占比。
    正常立绘只有两只脚（占比低）；模型顺手画了地板/色带时这个值会接近 1。"""
    import numpy as np
    a = np.asarray(im)[..., 3]
    h, w = a.shape
    k = max(1, int(h * rows))
    return float((a[h - k:] > thresh).mean())


def qa(path):
    from PIL import Image
    import statistics
    im = Image.open(path)
    stat = {"file": os.path.basename(path), "size": im.size, "mode": im.mode}
    small = im.convert("L").resize((64, 64))
    vals = list(small.getdata())
    stat["mean"] = round(statistics.mean(vals), 1)
    stat["stdev"] = round(statistics.pstdev(vals), 1)
    if im.mode == "RGBA":
        a = im.getchannel("A").resize((64, 64))
        cover = sum(1 for v in a.getdata() if v > 24) / (64 * 64)
        stat["alphaCoverage"] = round(cover, 3)
        stat["ok"] = stat["stdev"] > 8 and 0.10 < cover < 0.80
    else:
        stat["ok"] = stat["stdev"] > 8
    return stat


# ---------------------------------------------------------------- 主流程
def generate(prompt, negative, ckpt, seed, w, h, prefix, guide=None, strength=0.6, raw=None,
             steps=None, cfg=None, end_percent=0.62):
    if guide:
        gname = upload(guide)
        g = graph_guided(prompt, negative, ckpt, seed, gname, w, h, strength, prefix, steps, cfg, end_percent)
    else:
        g = graph_plain(prompt, negative, ckpt, seed, w, h, prefix, steps, cfg)
    pid = queue(g)
    entry = wait(pid)
    imgs = []
    for node in entry.get("outputs", {}).values():
        imgs += node.get("images", [])
    if not imgs:
        raise RuntimeError("ComfyUI 没有返回图像")
    fetch(imgs[0], raw)
    return raw


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--kind", choices=["bg", "char", "title"], required=True)
    ap.add_argument("--only", nargs="*", default=None)
    ap.add_argument("--all", action="store_true", help="生成全部（与 --only 互斥，缺省即全部）")
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--seed", type=int, default=19170718)
    ap.add_argument("--strength", type=float, default=0.60,
                    help="ControlNet 强度：越高构图越死板（背景默认 0.60）")
    ap.add_argument("--steps", type=int, default=STEPS)
    ap.add_argument("--cfg", type=float, default=CFG)
    ap.add_argument("--ref", default="", help="角色参考图（IPAdapter）。绝不要传背景图。")
    ap.add_argument("--no-guide", action="store_true", help="立绘不使用人形引导图")
    ap.add_argument("--reuse-raw", action="store_true",
                    help="ToolsOut/raw 里已有原图时跳过生成，只重做抠图后处理（调试用）")
    ap.add_argument("--key", choices=sorted(KEYS.keys()), default=None,
                    help="抠图键色（默认按角色自动选择）")
    ap.add_argument("--tries", type=int, default=4,
                    help="立绘重画次数：幕布色相不符合要求时换种子重来")
    args = ap.parse_args()

    report = []
    os.makedirs(OUT_RAW, exist_ok=True)

    ref_name = None
    if args.ref:
        if "/Backgrounds/" in args.ref.replace("\\", "/") or "\\Backgrounds\\" in args.ref:
            print("[WARN] 传入的是背景图！这会把背景烧进每一张立绘（v1 翻车的原因）。已忽略。")
        else:
            ref_name = os.path.basename(args.ref) if not os.path.exists(args.ref) else upload(args.ref)

    if args.kind == "bg":
        targets = list(BG_SPECS.keys())
    elif args.kind == "char":
        targets = list(CHAR_SPECS.keys())
    else:
        targets = ["title_styles"]
    if args.only:
        targets = [t for t in targets if t in args.only]
    if args.limit:
        targets = targets[:args.limit]

    for i, name in enumerate(targets):
        seed = args.seed + i * 977
        try:
            if args.kind == "bg":
                spec = BG_SPECS[name]
                guide = build_bg_guide(spec, name)
                prompt = spec["prompt"] + ", " + STYLE
                raw = os.path.join(OUT_RAW, name + ".raw.png")
                generate(prompt, STYLE_NEG, CKPT_BG, seed, BG_W, BG_H,
                         "styles_bg", guide, args.strength, raw)
                dst = os.path.join(ART, "Backgrounds", name + ".png")
                os.replace(raw, dst)
            elif args.kind == "char":
                key = args.key or CHAR_KEYS.get(name, DEFAULT_KEY)
                guide = None if args.no_guide else build_figure_guide(name)
                prompt = CHAR_SPECS[name] + ", " + CHAR_STYLE + ", " + KEYS[key]["prompt"]
                raw = os.path.join(OUT_RAW, name + ".raw.png")
                dst = os.path.join(ART, "Characters", name + "_neutral.png")
                # 出图有两道自动闸门，不合格就换种子重来：
                #   ① 幕布色相必须贴着要求的键色（否则抠图会把人物一起咬掉）
                #   ② 抠完以后的「不透明覆盖率」要在合理区间（太低=人物被衣服同色吃掉）
                want, want_tol = KEYS[key]["want"], KEYS[key]["want_tol"]
                im, cut = None, None
                for attempt in range(max(1, args.tries)):
                    s = seed + attempt * 7919
                    if not (args.reuse_raw and os.path.exists(raw)):
                        generate(prompt, CHAR_NEG + ", " + KEYS[key]["negative"], CKPT_CHAR, s,
                                 CHAR_W, CHAR_H, "styles_char", guide, min(args.strength, 0.35), raw,
                                 steps=args.steps, cfg=args.cfg, end_percent=0.45)
                    got, _ = measure_bg_hue(raw)
                    gap = abs((got - want + 180) % 360 - 180)
                    im, cut = cutout_alpha(raw, key=key)
                    cov = cut["coverage"]
                    band = bottom_band_ratio(im)
                    cut["bottomBand"] = round(band, 3)
                    # 大面积「非主体」残留 = 模型在人物背后画了一块投影
                    shadow = cut.get("speckPixels", 0) > 20000
                    if gap <= want_tol and 0.12 <= cov <= 0.80 and band <= 0.62 and not shadow:
                        break
                    if gap > want_tol:
                        reason = "幕布色相 %.0f° 偏 %.0f°" % (got, gap)
                    elif not (0.12 <= cov <= 0.80):
                        reason = "抠完只剩 %.1f%% 不透明" % (cov * 100)
                    elif shadow:
                        reason = "人物背后拖了一块投影（%d 像素残留）" % cut.get("speckPixels", 0)
                    else:
                        reason = "模型在脚下画了地板（底边占满 %.0f%%）" % (band * 100)
                    print("      试画 %d 不合格（%s），换种子重画" % (attempt + 1, reason), flush=True)
                    if args.reuse_raw and os.path.exists(raw):
                        os.remove(raw)          # 丢掉这张原图，下一轮才会真的重画
                if im is None:
                    raise RuntimeError("连续 %d 次都没画好" % args.tries)
                im = despill(im, key)
                im = normalize_sprite(im)
                os.makedirs(os.path.dirname(dst), exist_ok=True)
                im.save(dst)
            else:
                prompt = TITLE_SPEC["prompt"] + ", " + TITLE_STYLE
                raw = os.path.join(OUT_RAW, name + ".raw.png")
                generate(prompt, TITLE_NEG, CKPT_BG, seed, TITLE_W, TITLE_H,
                         "styles_title", None, args.strength, raw,
                         steps=args.steps, cfg=args.cfg)
                dst = os.path.join(ART, "Backgrounds", name + ".png")
                os.replace(raw, dst)
            st = qa(dst)
            st["name"] = name
            if args.kind == "char":
                st["cutout"] = cut
            report.append(st)
            print("[OK] %-22s %s" % (name, st), flush=True)
        except Exception as e:
            print("[FAIL] %-20s %s" % (name, e), flush=True)
            report.append({"name": name, "error": str(e), "ok": False})

    os.makedirs(os.path.dirname(OUT_MANIFEST), exist_ok=True)
    rp = os.path.join(ROOT, "ToolsOut", "qa_report.json")
    old = []
    if os.path.exists(rp):
        try:
            old = [r for r in json.load(open(rp, encoding="utf-8"))
                   if r.get("name") not in {x.get("name") for x in report}]
        except Exception:
            old = []
    json.dump(old + report, open(rp, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    bad = [r for r in report if not r.get("ok")]
    print("质检报告: %s   本批 %d 张，不合格 %d 张" % (rp, len(report), len(bad)))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
