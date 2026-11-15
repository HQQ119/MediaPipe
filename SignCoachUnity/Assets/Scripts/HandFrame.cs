using System;
using UnityEngine;

namespace SignCoach
{
    [Serializable] public class Landmark { public int id; public float x, y, z; }
    [Serializable] public class HandData
    {
        public string handedness;
        public float handedness_score;
        public Landmark[] landmarks;
    }
    [Serializable] public class HandFrame { public long timestamp; public HandData[] hands; }

    public static class HandProtocol
    {
        public static HandFrame Parse(string json)
        {
            var frame = JsonUtility.FromJson<HandFrame>(json);
            if (frame == null || frame.timestamp < 0 || frame.hands == null || frame.hands.Length > 2)
                throw new FormatException("Missing/invalid frame fields");
            foreach (var hand in frame.hands)
            {
                if (hand == null || hand.landmarks == null || hand.landmarks.Length != 21 ||
                    (hand.handedness != "Left" && hand.handedness != "Right" && hand.handedness != "Unknown"))
                    throw new FormatException("Invalid hand or landmark count");
                var seen = new bool[21];
                foreach (var point in hand.landmarks)
                {
                    if (point == null || point.id < 0 || point.id >= 21 || seen[point.id] ||
                        !Finite(point.x) || !Finite(point.y) || !Finite(point.z))
                        throw new FormatException("Invalid landmark id/coordinate");
                    seen[point.id] = true;
                }
            }
            return frame;
        }

        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

        // JSON 是未镜像图像坐标；仅显示时镜像一次，Y 翻转到 Unity 向上坐标。
        public static Vector3 Position(Landmark point)
        {
            return new Vector3((0.5f - point.x) * 6.4f, (0.5f - point.y) * 4.8f, 0);
        }
    }
}
