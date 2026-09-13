using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Styles.Core;
using UnityEditor;
using UnityEngine;

namespace Styles.EditorTools
{
    /// <summary>
    /// 内容与素材体检：剧本引用是否完整、缺哪些图、文本量对应多少游玩时长。
    /// 输出 ToolsOut/content_report.json 与 ToolsOut/asset_manifest.json，供 ComfyUI 生产线消费。
    /// </summary>
    public static class ContentValidator
    {
        // 一律使用绝对路径：批处理模式下的工作目录未必是工程根目录
        static string ProjectRoot { get { return System.IO.Path.GetDirectoryName(Application.dataPath); } }
        static string ContentRoot { get { return System.IO.Path.Combine(ProjectRoot, "Assets/StreamingAssets/content"); } }
        static string ArtRoot { get { return System.IO.Path.Combine(ProjectRoot, "Assets/Resources/Art"); } }
        static string OutDir { get { return System.IO.Path.Combine(ProjectRoot, "ToolsOut"); } }

        [MenuItem("Styles/5. 校验内容与素材", false, 5)]
        public static void ValidateMenu() { ValidateAll(true); }

        /// <summary>路径诊断：确认批处理模式下的工作目录与素材根目录是否如预期。</summary>
        [MenuItem("Styles/9. 诊断路径", false, 90)]
        public static void Diagnose()
        {
            var bgDir = Path.Combine(ArtRoot, "Backgrounds");
            var files = Directory.Exists(bgDir) ? Directory.GetFiles(bgDir) : new string[0];
            Debug.Log("[Diag] dataPath=" + Application.dataPath);
            Debug.Log("[Diag] cwd=" + Directory.GetCurrentDirectory());
            Debug.Log("[Diag] ArtRoot=" + ArtRoot + "  dirExists=" + Directory.Exists(ArtRoot));
            Debug.Log("[Diag] ContentRoot=" + ContentRoot + "  dirExists=" + Directory.Exists(ContentRoot));
            Debug.Log("[Diag] Backgrounds 文件数=" + files.Length + (files.Length > 0 ? "  首个=" + files[0] : ""));
            var probe = Path.Combine(ArtRoot, "Backgrounds/styles_bedroom.png");
            Debug.Log("[Diag] 探测 " + probe + " → " + File.Exists(probe));
            // 字体资产体检
            var fa = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/Resources/Fonts/StylesSerif SDF.asset");
            if (fa == null) Debug.LogError("[Diag] 字体资产载入失败");
            else
            {
                var texOk = fa.atlasTextures != null && fa.atlasTextures.Length > 0 && fa.atlasTextures[0] != null;
                Debug.Log("[Diag] 字体=" + fa.name + " 图集数=" + (fa.atlasTextures == null ? -1 : fa.atlasTextures.Length) +
                          " 图集0=" + (texOk ? fa.atlasTextures[0].width + "x" + fa.atlasTextures[0].height : "空") +
                          " 材质=" + (fa.material != null ? fa.material.name : "空") +
                          " 着色器=" + (fa.material != null && fa.material.shader != null ? fa.material.shader.name : "空") +
                          " 源字体=" + (fa.sourceFontFile != null ? fa.sourceFontFile.name : "空") +
                          " 动态=" + fa.atlasPopulationMode);
                var settings = TMPro.TMP_Settings.instance;
                Debug.Log("[Diag] TMP Settings=" + (settings != null ? "已加载" : "缺失") +
                          " 默认字体=" + (TMPro.TMP_Settings.defaultFontAsset != null ? TMPro.TMP_Settings.defaultFontAsset.name : "空"));
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        public static bool ValidateAll(bool writeReport)
        {
            var problems = new List<string>();
            var warnings = new List<string>();
            var db = LoadLocal();
            if (db == null) { Debug.LogError("[Styles] 内容读取失败，无法校验。"); return false; }

            var beats = db.Flatten();
            var labels = new HashSet<string>();
            foreach (var b in beats) if (b.t == "label" && !string.IsNullOrEmpty(b.id)) labels.Add(b.id);

            var needBg = new HashSet<string>();
            var needChar = new HashSet<string>();
            var needCg = new HashSet<string>();
            int textChars = 0, sayCount = 0, deduceCount = 0, invCount = 0, askCount = 0, choiceCount = 0;
            var perChapter = new List<string>();

            foreach (var ch in db.Chapters)
            {
                int chChars = 0, chBeats = 0;
                foreach (var b in ch.beats)
                {
                    chBeats++;
                    if (b.t == "say")
                    {
                        sayCount++;
                        chChars += (b.text ?? "").Length;
                        if (!string.IsNullOrEmpty(b.who) && b.who != "narr" && !db.Characters.ContainsKey(b.who))
                            problems.Add(ch.id + "：未知说话人 " + b.who);
                        if (b.figs != null)
                            foreach (var f in b.figs)
                            {
                                needChar.Add(f.id + "|" + (string.IsNullOrEmpty(f.emotion) ? "neutral" : f.emotion));
                                if (!db.Characters.ContainsKey(f.id)) problems.Add(ch.id + "：未知立绘 " + f.id);
                            }
                    }
                    if (b.t == "deduce")
                    {
                        deduceCount++;
                        if (b.answers == null || b.answers.Count < 2) problems.Add(ch.id + "：推理题选项不足");
                        if (b.answer < 0 || b.answers == null || b.answer >= b.answers.Count) problems.Add(ch.id + "：推理题答案越界：「" + b.question + "」");
                        if (!string.IsNullOrEmpty(b.give) && !db.Evidence.ContainsKey(b.give)) problems.Add(ch.id + "：未知证据 " + b.give);
                        if (!string.IsNullOrEmpty(b.insight) && !db.Evidence.ContainsKey(b.insight)) problems.Add(ch.id + "：未知批注 " + b.insight);
                    }
                    if (b.t == "choice")
                    {
                        choiceCount++;
                        if (b.options == null || b.options.Count == 0) problems.Add(ch.id + "：空选项组");
                        else foreach (var o in b.options)
                            {
                                if (!string.IsNullOrEmpty(o.give) && !db.Evidence.ContainsKey(o.give)) problems.Add(ch.id + "：未知证据 " + o.give);
                                if (!string.IsNullOrEmpty(o.gotoLabel) && !labels.Contains(o.gotoLabel)) problems.Add(ch.id + "：跳转标签不存在 " + o.gotoLabel);
                            }
                    }
                    if (b.t == "investigate")
                    {
                        invCount++;
                        var key = !string.IsNullOrEmpty(b.scene) ? b.scene : b.sceneId;
                        InvestigationScene sc;
                        if (!db.Investigations.TryGetValue(key ?? "", out sc)) problems.Add(ch.id + "：未知调查场景 " + key);
                        else
                        {
                            needBg.Add(sc.bg);
                            if (b.need > sc.spots.Count) problems.Add(ch.id + "：调查点需求超过热点数量");
                            foreach (var sp in sc.spots)
                                if (!string.IsNullOrEmpty(sp.clue) && !db.Evidence.ContainsKey(sp.clue))
                                    problems.Add(ch.id + "：调查点引用未知证据 " + sp.clue);
                        }
                    }
                    if (b.t == "ask")
                    {
                        askCount++;
                        if (b.chars != null)
                            foreach (var cid in b.chars)
                            {
                                AskCharacter a;
                                if (!db.Interrogations.TryGetValue(cid, out a)) problems.Add(ch.id + "：未知询问对象 " + cid);
                                else foreach (var t in a.topics)
                                    {
                                        if (!string.IsNullOrEmpty(t.give) && !db.Evidence.ContainsKey(t.give)) problems.Add(ch.id + "：询问线索不存在 " + t.give);
                                        if (!string.IsNullOrEmpty(t.need) && !db.Evidence.ContainsKey(t.need)) problems.Add(ch.id + "：前置线索不存在 " + t.need);
                                    }
                            }
                    }
                    if (b.t == "bg" || b.t == "investigate")
                    {
                        if (!string.IsNullOrEmpty(b.id)) needBg.Add(b.id);
                    }
                    if (b.t == "jump" && !string.IsNullOrEmpty(b.target) && !labels.Contains(b.target))
                        problems.Add(ch.id + "：跳转标签不存在 " + b.target);
                    if (b.t == "accuse" || b.t == "ending") { }
                }
                textChars += chChars;
                perChapter.Add(ch.id + " | " + ch.title + " | " + chBeats + " 条 | " + chChars + " 字 | 约 " +
                               Mathf.RoundToInt(chChars / 250f) + " 分钟");
            }

            // 未在剧本中出现的证据（提醒）。
            // 注意：证据有四条发放通道——指令上的 give/insight、选项的 give、
            // 现场调查的热点、询问话题的 give。少算任何一条都会误报一堆「证据未被授予」。
            foreach (var kv in db.Evidence)
            {
                bool used = false;
                foreach (var b in beats)
                {
                    if (b.give == kv.Key || b.insight == kv.Key) { used = true; break; }
                    if (b.options != null) foreach (var o in b.options) if (o.give == kv.Key) { used = true; break; }
                    if (used) break;
                    if (b.t == "investigate")
                    {
                        var key = !string.IsNullOrEmpty(b.scene) ? b.scene : b.sceneId;
                        InvestigationScene inv;
                        if (db.Investigations.TryGetValue(key ?? "", out inv))
                            foreach (var sp in inv.spots)
                                if (sp.clue == kv.Key) { used = true; break; }
                    }
                    if (used) break;
                    if (b.t == "ask" && b.chars != null)
                        foreach (var cid in b.chars)
                        {
                            AskCharacter ask;
                            if (db.Interrogations.TryGetValue(cid, out ask))
                                foreach (var t in ask.topics)
                                    if (t.give == kv.Key) { used = true; break; }
                            if (used) break;
                        }
                }
                if (!used) warnings.Add("证据未被授予：" + kv.Key + "（" + kv.Value.name + "）");
            }

            // 定义了却没有任何指令引用的调查场景（作者很容易忘记接上）
            foreach (var kv in db.Investigations)
            {
                bool referenced = false;
                foreach (var b in beats)
                    if (b.t == "investigate")
                    {
                        var key = !string.IsNullOrEmpty(b.scene) ? b.scene : b.sceneId;
                        if (key == kv.Key) { referenced = true; break; }
                    }
                if (!referenced)
                    warnings.Add("调查场景从未被剧本触发：" + kv.Key + "（" + kv.Value.title + "）");
            }

            // 素材清单
            var missingArt = new List<string>();
            foreach (var bg in needBg)
                if (!HasArt("Backgrounds/" + bg)) missingArt.Add("Backgrounds/" + bg + ".png");
            // 标题画面的底图不写在剧本里，但缺了它标题就只剩一片深色
            if (!HasArt("Backgrounds/title_styles")) warnings.Add("缺少标题底图：Backgrounds/title_styles.png");
            foreach (var c in needChar)
            {
                var parts = c.Split('|');
                var file = "Characters/" + parts[0] + "_" + parts[1] + ".png";
                if (!HasArt(file) && !HasArt("Characters/" + parts[0] + "_neutral.png")) missingArt.Add(file);
            }

            int minutes = Mathf.RoundToInt(textChars / 250f);
            var sb = new StringBuilder();
            sb.AppendLine("=== 《斯泰尔斯庄园奇案》内容报告 ===");
            sb.AppendLine("章节 " + db.Chapters.Count + " · 演出指令 " + beats.Count + " 条（对白 " + sayCount + "）");
            sb.AppendLine("推理题 " + deduceCount + " · 现场调查 " + invCount + " · 询问场 " + askCount + " · 选择 " + choiceCount);
            sb.AppendLine("正文 " + textChars + " 字 ≈ " + (minutes / 60f).ToString("0.0") + " 小时（按 250 字/分钟）");
            sb.AppendLine("证据 " + db.Evidence.Count + " 条 · 人物 " + db.Characters.Count + " · 指认问题 " + db.Accusation.Count);
            sb.AppendLine("需要背景 " + needBg.Count + " 张 · 需要立绘 " + needChar.Count + " 张");
            sb.AppendLine();
            sb.AppendLine("--- 分章统计 ---");
            foreach (var l in perChapter) sb.AppendLine(l);
            sb.AppendLine();
            sb.AppendLine("--- 缺失素材 " + missingArt.Count + " ---");
            foreach (var m in missingArt) sb.AppendLine(m);
            sb.AppendLine();
            sb.AppendLine("--- 校验问题 " + problems.Count + " ---");
            foreach (var p in problems) sb.AppendLine(p);
            sb.AppendLine();
            sb.AppendLine("--- 提醒 " + warnings.Count + " ---");
            foreach (var w in warnings) sb.AppendLine(w);
            var report = sb.ToString();

            if (writeReport)
            {
                Directory.CreateDirectory(OutDir);
                File.WriteAllText(Path.Combine(OutDir, "content_report.txt"), report, Encoding.UTF8);
                var manifest = new AssetManifest
                {
                    backgrounds = new List<string>(needBg),
                    characters = new List<string>(needChar),
                    cgs = new List<string>(needCg),
                    missing = missingArt
                };
                File.WriteAllText(Path.Combine(OutDir, "asset_manifest.json"),
                    JsonConvert.SerializeObject(manifest, Formatting.Indented), Encoding.UTF8);
            }
            Debug.Log(report);
            return problems.Count == 0;
        }

        static bool HasArt(string relative)
        {
            // 允许传入不带扩展名的 id，也允许带扩展名
            var p = Path.Combine(ArtRoot, relative);
            if (File.Exists(p)) return true;
            if (File.Exists(p + ".png") || File.Exists(p + ".jpg")) return true;
            var noExt = Path.ChangeExtension(p, null);
            return File.Exists(noExt + ".png") || File.Exists(noExt + ".jpg");
        }

        static ContentDatabase LoadLocal()
        {
            try
            {
                var db = new ContentDatabase();
                var index = JsonConvert.DeserializeObject<ContentIndex>(File.ReadAllText(Path.Combine(ContentRoot, "index.json")));
                if (index == null) return null;
                foreach (var f in index.chapters) db.Chapters.Add(JsonConvert.DeserializeObject<Chapter>(File.ReadAllText(Path.Combine(ContentRoot, f))));
                foreach (var f in index.characters)
                    foreach (var c in JsonConvert.DeserializeObject<List<Styles.Core.CharacterInfo>>(File.ReadAllText(Path.Combine(ContentRoot, f)))) db.Characters[c.id] = c;
                foreach (var f in index.evidence)
                    foreach (var c in JsonConvert.DeserializeObject<List<EvidenceInfo>>(File.ReadAllText(Path.Combine(ContentRoot, f)))) db.Evidence[c.id] = c;
                foreach (var f in index.investigations)
                    foreach (var c in JsonConvert.DeserializeObject<List<InvestigationScene>>(File.ReadAllText(Path.Combine(ContentRoot, f)))) db.Investigations[c.id] = c;
                foreach (var f in index.interrogations)
                    foreach (var c in JsonConvert.DeserializeObject<List<AskCharacter>>(File.ReadAllText(Path.Combine(ContentRoot, f)))) db.Interrogations[c.id] = c;
                db.Accusation.AddRange(JsonConvert.DeserializeObject<List<AccusationQuestion>>(File.ReadAllText(Path.Combine(ContentRoot, "accusation.json"))));
                db.Faith.AddRange(JsonConvert.DeserializeObject<List<FaithRow>>(File.ReadAllText(Path.Combine(ContentRoot, "faith.json"))));
                return db;
            }
            catch (Exception e) { Debug.LogError("[Styles] 内容读取异常：" + e.Message); return null; }
        }

        [Serializable]
        public class AssetManifest
        {
            public List<string> backgrounds = new List<string>();
            public List<string> characters = new List<string>();
            public List<string> cgs = new List<string>();
            public List<string> missing = new List<string>();
        }
    }
}
