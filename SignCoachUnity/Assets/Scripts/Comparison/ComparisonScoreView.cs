using System;
using System.Globalization;
using UnityEngine;

namespace SignCoach.Comparison
{
    // 只呈现原有DTW相似度，不重新计算、校准或改变质量判断。
    public static class ComparisonScoreView
    {
        public static bool HasScore(ComparisonResult result)
        {
            return result != null && result.score_available && !double.IsNaN(result.similarity) && !double.IsInfinity(result.similarity)
                && result.similarity >= 0 && result.similarity <= 100;
        }
        public static string ScoreText(ComparisonResult result)
        {
            return HasScore(result) ? result.similarity.ToString("F1", CultureInfo.InvariantCulture) : "—";
        }
        public static string ScopeText(ComparisonResult result)
        {
            if (!HasScore(result)) return result!=null&&!string.IsNullOrEmpty(result.score_unavailable_reason)?result.score_unavailable_reason:"暂无可用分数，请重新选择动作或录制。";
            if(result.low_quality)
            {
                bool reference=result.quality_a!=null&&result.quality_a.low_quality,practice=result.quality_b!=null&&result.quality_b.low_quality;
                if(reference&&!practice)return "参考片段质量不足：仅有效片段的参考分，不能据此评价你的动作。建议更换参考。";
                if(practice&&!reference)return "练习区间数据不足：仅有效片段的参考分，建议重新录制练习。";
                return "数据质量不足：仅有效片段的参考分，请检查参考片段与练习数据。";
            }
            return "分数越高，已对齐的手部动作越接近。仅供动作对照，不是手语正确率。";
        }
        public static string FormulaText(ComparisonResult result)
        {
            if (result == null || result.settings == null) return "尚无计算结果。";
            bool intro=result.settings.ScoringProfile==ComparisonScoringProfile.Introductory;
            return (intro?"入门档：r = 差异/容差，单项得分 = 100/(1+k×r²)，k = 1/容差得分比例−1；达到配置容差时单项保留 "+(100*result.settings.IntroductoryToleranceCredit).ToString("F0")+"分。":"精细档：每项损失 = d/(d+容差)，达到容差时单项为50分。")
                +"沿用精细DTW对齐路径，各项得分按权重合成；总分 = 100 × (1 − 平均损失) × 动静一致系数。切换档位不改变原始距离与对齐。"
                + "关节角容差 "+(180*result.settings.AngleTolerance).ToString("F0")+"°、掌向 "+(180*result.settings.DirectionTolerance).ToString("F0")
                +"°，其余按掌尺度计量。容差是工程参数，尚未经过手语教学样本校准。";
        }
        public static void DrawBreakdown(ComparisonResult result)
        {
            if(result==null||result.component_scores==null||result.component_scores.Length!=6)return;
            string[] names={"手指弯曲","指尖间距","局部手型","手掌朝向","移动轨迹","尺度变化"};
            for(int i=0;i<6;i+=2)GUILayout.Label(names[i]+"："+result.component_scores[i].ToString("F1")+" 分　　"+names[i+1]+"："+result.component_scores[i+1].ToString("F1")+" 分");
            if(result.compares_both_hands)GUILayout.Label("双手相对位置："+result.relative_hands_score.ToString("F1")+" 分");
            GUILayout.Label("动作动静一致性："+result.motion_presence_score.ToString("F1")+" 分");
            GUILayout.Label(result.hand_pairing+(result.reference_mirrored?"；用户指定还原参考水平镜像":""));
        }
        public static void Draw(ComparisonResult result, Font font = null)
        {
            bool limited = result != null && result.low_quality;
            var accent = limited ? new Color(.59f, .36f, .08f) : new Color(.12f, .38f, .30f);
            var background = limited ? new Color(1f, .96f, .88f) : new Color(.91f, .96f, .93f);
            var area = GUILayoutUtility.GetRect(100, 190, GUILayout.ExpandWidth(true));
            Paint(area, background);
            var title = new GUIStyle(GUI.skin.label) { font = font ?? GUI.skin.font, fontSize = 18, fontStyle = FontStyle.Bold };
            var number = new GUIStyle(title) { fontSize = area.width < 500 ? 36 : 48, normal = { textColor = accent } };
            var note = new GUIStyle(title) { fontSize = 14, fontStyle = FontStyle.Normal, wordWrap = true, normal = { textColor = accent } };
            string profile=result!=null&&result.settings!=null&&result.settings.ScoringProfile==ComparisonScoringProfile.Exact?"精细对照":"入门练习";
            GUI.Label(new Rect(area.x + 18, area.y + 10, area.width - 36, 28),
                profile+(limited?" · 有效片段参考分":" · 动作相似度分数"), title);
            GUI.Label(new Rect(area.x + 18, area.y + 38, area.width - 36, 68), ScoreText(result) + " / 100 分", number);
            var bar = new Rect(area.x + 20, area.y + 118, Math.Max(1, area.width - 40), 10);
            Paint(bar, new Color(.82f, .86f, .83f));
            if (HasScore(result)) Paint(new Rect(bar.x, bar.y, bar.width * (float)(result.similarity / 100), bar.height), accent);
            GUI.Label(new Rect(area.x + 18, area.y + 143, area.width - 36, 42), ScopeText(result), note);
        }
        static void Paint(Rect area, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
