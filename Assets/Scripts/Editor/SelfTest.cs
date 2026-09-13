using System;
using System.Collections.Generic;
using System.Text;
using Styles.Core;
using UnityEditor;
using UnityEngine;

namespace Styles.EditorTools
{
    /// <summary>
    /// 自动化通关自检：用假的界面层把整部剧本从头跑到结局，
    /// 校验解释器、条件分支、标签跳转、证据发放、指认流程是否自洽。
    /// </summary>
    public static class SelfTest
    {
        [MenuItem("Styles/7. 自动通关自检", false, 7)]
        public static void Run() { RunInternal(true); }

        public static void RunBatch() { RunInternal(true); }

        class MockView : IVnView
        {
            public int Says, Choices, Deduces, Investigates, Asks, Notes, Chapters;
            public int WrongDeduceTries;
            public bool Ended;
            public readonly List<string> Errors = new List<string>();
            public readonly HashSet<string> SeenBg = new HashSet<string>();
            public readonly HashSet<string> SeenFig = new HashSet<string>();

            // 用「待执行动作」代替直接递归调用，避免 300+ 层同步递归把栈压爆
            public Action Pending;

            public void ShowSay(Beat b, Action next) { Says++; Pending = next; }
            public void ShowChapter(Beat b, Action next) { Chapters++; Pending = next; }
            public void ShowChoice(Beat b, Action<Option> pick)
            {
                Choices++;
                if (b.options == null || b.options.Count == 0) { Errors.Add("空选项组"); Pending = () => pick(null); return; }
                Pending = () => pick(b.options[0]);
            }
            public void ShowDeduce(Beat b, Action<int> resolve)
            {
                Deduces++;
                if (b.answers == null || b.answer < 0 || b.answer >= b.answers.Count)
                {
                    Errors.Add("推理题答案越界：" + b.question);
                    Pending = () => resolve(0);
                    return;
                }
                Pending = () => resolve(b.answer);
            }
            public void ShowInvestigate(InvestigationScene sc, Action done)
            {
                Investigates++;
                var st = GameDirector.Instance.State;
                for (int i = 0; i < sc.spots.Count; i++)
                {
                    st.spots[sc.id + "_" + i] = true;
                    if (!string.IsNullOrEmpty(sc.spots[i].clue)) st.AddClue(sc.spots[i].clue);
                }
                Pending = done;
            }
            public void ShowAsk(List<AskCharacter> chars, int need, string title, Action done)
            {
                Asks++;
                var st = GameDirector.Instance.State;
                foreach (var c in chars)
                    foreach (var t in c.topics)
                    {
                        st.topics[c.id + "_" + t.id] = true;
                        if (!string.IsNullOrEmpty(t.give)) st.AddClue(t.give);
                    }
                Pending = done;
            }
            public void ShowNote(Beat b, Action done) { Notes++; Pending = done; }
            public void ShowAccusation(List<AccusationQuestion> qs, Action<List<AccuseRecord>> done)
            {
                var recs = new List<AccuseRecord>();
                foreach (var q in qs)
                    recs.Add(new AccuseRecord
                    {
                        question = q.question,
                        pick = q.options[q.answer],
                        truth = q.options[q.answer],
                        ok = true,
                        why = q.explain
                    });
                Pending = () => done(recs);
            }
            public void ShowEnding(GameState st, ContentDatabase db) { Ended = true; }
            public void SetBackground(string id, bool instant) { SeenBg.Add(id); }
            public void SetFigures(List<FigureState> figs) { if (figs != null) foreach (var f in figs) SeenFig.Add(f.id + "|" + f.emotion); }
            public void PlayFx(string fx) { }
            public void Toast(string msg) { }
            public void SetHud(string tag, string title) { }
        }

        static void RunInternal(bool exit)
        {
            var sb = new StringBuilder();
            int exitCode = 0;
            GameObject go = null;
            try
            {
                var db = ContentDatabase.Load(new StreamingAssetsContentSource());
                var view = new MockView();
                go = new GameObject("SelfTest");
                var dir = go.AddComponent<GameDirector>();
                dir.Initialize(db, view, null);
                dir.StartNewGame();

                // 迭代驱动：每次执行界面层留下的待办动作
                int guard = 200000;
                while (!view.Ended && guard-- > 0)
                {
                    var act = view.Pending;
                    view.Pending = null;
                    if (act == null) break;
                    act();
                }

                var st = dir.State;
                sb.AppendLine("=== 自动通关自检 ===");
                sb.AppendLine("章节 " + db.Chapters.Count + " · 演出指令 " + db.Flatten().Count);
                sb.AppendLine("对白 " + view.Says + " · 选项 " + view.Choices + " · 推理 " + view.Deduces +
                              " · 调查 " + view.Investigates + " · 询问 " + view.Asks + " · 知识卡 " + view.Notes +
                              " · 章节卡 " + view.Chapters);
                sb.AppendLine("用过的背景 " + view.SeenBg.Count + " 种 · 立绘 " + view.SeenFig.Count + " 种");
                sb.AppendLine("收集证据 " + st.clues.Count + " / " + db.Evidence.Count);
                sb.AppendLine("指认 " + st.accuse.Count + " 次，全对 " + (st.accuse.TrueForAll(a => a.ok) ? "是" : "否"));
                sb.AppendLine("结局触发 " + (view.Ended ? "是" : "否"));

                // 校验缺失的素材引用
                var missingBg = new List<string>();
                foreach (var bg in view.SeenBg)
                    if (AssetService.Background(bg) == AssetService.Missing()) missingBg.Add(bg);
                var missingFig = new List<string>();
                foreach (var f in view.SeenFig)
                {
                    var p = f.Split('|');
                    if (AssetService.Portrait(p[0], p[1]) == AssetService.Missing()) missingFig.Add(f);
                }
                if (missingBg.Count > 0) { sb.AppendLine("缺背景: " + string.Join(", ", missingBg)); exitCode = 1; }
                if (missingFig.Count > 0) { sb.AppendLine("缺立绘: " + string.Join(", ", missingFig)); exitCode = 1; }
                if (!view.Ended) { sb.AppendLine("错误：剧本没有走到结局"); exitCode = 1; }
                if (st.clues.Count < 20) { sb.AppendLine("警告：证据收集过少"); }
                foreach (var e in view.Errors) sb.AppendLine("错误：" + e);
                if (view.Errors.Count > 0) exitCode = 1;
                sb.AppendLine(exitCode == 0 ? "结果：通过 ✔" : "结果：有问题 ✘");
            }
            catch (Exception e)
            {
                sb.AppendLine("异常：" + e);
                exitCode = 2;
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            Debug.Log(sb.ToString());
            if (exit && Application.isBatchMode) EditorApplication.Exit(exitCode);
        }
    }
}
