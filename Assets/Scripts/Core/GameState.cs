using System;
using System.Collections.Generic;

namespace Styles.Core
{
    [Serializable]
    public class AccuseRecord
    {
        public string question;
        public string pick;
        public string truth;
        public bool ok;
        public string why;
    }

    [Serializable]
    public class LogLine
    {
        public string who;
        public string text;
    }

    /// <summary>一次游玩过程中的全部可变状态；直接序列化为存档。</summary>
    [Serializable]
    public class GameState
    {
        public int version = 1;
        public string chapterId = "";
        public int beatIndex = 0;
        public List<string> clues = new List<string>();
        public List<string> insights = new List<string>();
        public Dictionary<string, int> flags = new Dictionary<string, int>();
        public Dictionary<string, bool> topics = new Dictionary<string, bool>();
        public Dictionary<string, bool> spots = new Dictionary<string, bool>();
        public List<AccuseRecord> accuse = new List<AccuseRecord>();
        public List<LogLine> log = new List<LogLine>();
        public int score = 0;
        public int mistakes = 0;
        public string bg = "styles_manor_dusk";
        public string bgm = "";
        public float playSeconds = 0f;
        public string savedAtUtc = "";

        public bool HasClue(string id) { return !string.IsNullOrEmpty(id) && clues.Contains(id); }

        public void AddClue(string id)
        {
            if (string.IsNullOrEmpty(id) || clues.Contains(id)) return;
            clues.Add(id);
        }

        public int Flag(string key)
        {
            int v;
            return flags != null && flags.TryGetValue(key, out v) ? v : 0;
        }

        public void SetFlag(string key, int value)
        {
            if (flags == null) flags = new Dictionary<string, int>();
            if (string.IsNullOrEmpty(key)) return;
            flags[key] = value;
        }

        public void Log(string who, string text)
        {
            if (log == null) log = new List<LogLine>();
            log.Add(new LogLine { who = who, text = text });
            if (log.Count > 600) log.RemoveRange(0, log.Count - 600);
        }

        public int Grade(int accusationTotal)
        {
            int right = 0;
            foreach (var a in accuse) if (a.ok) right++;
            bool mainWrong = accuse.Count > 0 && !accuse[0].ok;
            if (mainWrong) return 4;                        // D
            if (right == accusationTotal) return 0;          // S
            if (right >= accusationTotal - 1) return 1;      // A
            if (right >= Math.Max(2, accusationTotal / 2)) return 2; // B
            return 3;                                        // C
        }

        public static string GradeLetter(int g)
        {
            switch (g) { case 0: return "S"; case 1: return "A"; case 2: return "B"; case 3: return "C"; default: return "D"; }
        }
    }
}
