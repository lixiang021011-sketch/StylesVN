using UnityEngine;

namespace Styles.UI
{
    /// <summary>视觉基调：装饰艺术（Art Deco）。改这里即可整体换皮。</summary>
    public static class UiTheme
    {
        // 参考分辨率（16:9），移动端按比例缩放
        public const float RefWidth = 1920f;
        public const float RefHeight = 1080f;

        public static readonly Color Ink = Hex("#0B1310");
        public static readonly Color InkSoft = Hex("#132320");
        public static readonly Color Paper = Hex("#EFE3C8");
        public static readonly Color PaperDim = Hex("#BFB49A");
        public static readonly Color Gold = Hex("#C9A227");
        public static readonly Color GoldLight = Hex("#F0DDA0");
        public static readonly Color Wine = Hex("#6C1B2A");
        public static readonly Color Green = Hex("#1D4A3A");
        public static readonly Color Glass = new Color(0.04f, 0.08f, 0.07f, 0.90f);
        public static readonly Color GlassLight = new Color(0.06f, 0.12f, 0.11f, 0.72f);

        // 字号（参考分辨率下的像素）
        public const int FontTitle = 72;
        public const int FontChapter = 56;
        public const int FontBody = 30;
        public const int FontSmall = 22;
        public const int FontButton = 28;
        public const int FontHud = 24;

        public const float DialogueHeight = 300f;
        public const float DialogueMargin = 64f;
        public const float MinTouchSize = 88f;      // 移动端最小点击区域

        public static Color Hex(string hex)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.magenta;
        }

        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }
    }
}
