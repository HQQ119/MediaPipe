using System;
using UnityEngine;

namespace SignCoach
{
    public sealed class SignCoachDebug : MonoBehaviour
    {
        HandTcpClient client;
        HandSkeleton[] skeletons;
        Material left, right, unknown;
        Mesh dot;
        Camera view;
        HandFrame current;
        long seenRevision = -1;
        float lastFrameAt = -100, lastArrival = -100, receiveFps, renderFps;
        bool connected;
        string status = "Starting";
        public HandFrame Current { get { return current; } }
        public int VisibleHands { get { return skeletons == null ? 0 : (skeletons[0].Visible ? 1 : 0) + (skeletons[1].Visible ? 1 : 0); } }
        public long AppliedFrames { get; private set; }
        public RecordingPanel Recording { get; private set; }
        public SignCoach.Comparison.SequenceComparisonPanel Comparison { get; private set; }
        public LearningApp App { get; private set; }
        public bool IsReceiving { get { return connected && Time.realtimeSinceStartup - lastFrameAt <= .5f; } }

        void Awake()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            // 清除整个窗口，避免视口外的 FPS 等 GUI 文字逐帧叠印。
            var background = new GameObject("Background camera").AddComponent<Camera>();
            background.transform.SetParent(transform, false);
            background.clearFlags = CameraClearFlags.SolidColor;
            background.backgroundColor = new Color(.12f, .12f, .12f);
            background.cullingMask = 0; background.depth = -1;
            view = new GameObject("Skeleton camera").AddComponent<Camera>();
            view.transform.SetParent(transform, false); view.transform.localPosition = new Vector3(0, 0, -10);
            view.orthographic = true; view.orthographicSize = 2.4f;
            view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = new Color(.055f, .07f, .09f);
            left = CreateMaterial(new Color(.31f, .9f, .31f));
            right = CreateMaterial(new Color(.16f, .7f, 1)); unknown = CreateMaterial(Color.gray);
            dot = HandSkeleton.CreateDot();
            skeletons = new[] { new HandSkeleton(transform, unknown, dot, 0), new HandSkeleton(transform, unknown, dot, 1) };
            Recording = gameObject.AddComponent<RecordingPanel>(); Recording.Initialize(this);
            Comparison = gameObject.AddComponent<SignCoach.Comparison.SequenceComparisonPanel>(); Comparison.Initialize(Recording);
            App=gameObject.AddComponent<LearningApp>();App.Initialize(this);
            VerificationCapture.AttachIfRequested(gameObject);
        }
        Material CreateMaterial(Color color)
        {
            var material = new Material(Shader.Find("Unlit/Color")); material.color = color; return material;
        }
        void OnEnable()
        {
            client = new HandTcpClient();
            client.ValidFrameReceived += Recording.Recorder.Receive;
            client.StreamInterrupted += Recording.Recorder.Interrupt;
            client.Start();
        }
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0) renderFps = Mathf.Lerp(renderFps, 1 / dt, .1f);
            float availableHeight = Mathf.Max(1, Screen.height - 140);
            float availableWidth = Mathf.Max(1, Screen.width - RecordingPanel.PanelWidth - 24);
            float width = Mathf.Min(availableWidth, availableHeight * 4 / 3);
            float height = width * 3 / 4;
            view.rect = new Rect((availableWidth + 24 - width) / 2 / Screen.width, 16f / Screen.height, width / Screen.width, height / Screen.height);
            if(App!=null)
            {
                view.enabled=App.Page=="practice"&&App.Teaching==null;
                float w=Mathf.Max(1,Screen.width-RecordingPanel.PanelWidth-264),h=Mathf.Max(1,Screen.height-300);
                w=Mathf.Min(w,h*4/3);h=w*3/4;
                view.rect=new Rect(240f/Screen.width,90f/Screen.height,w/Screen.width,h/Screen.height);
            }
            HandFrame frame; long revision;
            client.Snapshot(out frame, out revision, out connected, out status);
            if (revision != seenRevision)
            {
                seenRevision = revision; current = frame;
                foreach (var skeleton in skeletons) skeleton.Hide();
                if (connected && frame != null)
                {
                    float now = Time.realtimeSinceStartup;
                    if (lastArrival > 0 && now > lastArrival) receiveFps = Mathf.Lerp(receiveFps, 1 / (now - lastArrival), .2f);
                    lastArrival = lastFrameAt = now;
                    for (int i = 0; i < frame.hands.Length; i++)
                    {
                        var hand = frame.hands[i];
                        // 数组槽位仅用于对象复用；标签和颜色始终读取 handedness。
                        var material = hand.handedness == "Left" ? left : hand.handedness == "Right" ? right : unknown;
                        skeletons[i].Show(hand, material);
                    }
                    AppliedFrames++;
                }
            }
            // 无数据、断线和空 hands 都不能留下上一帧的骨骼。
            if (!connected || Time.realtimeSinceStartup - lastFrameAt > .5f)
            {
                foreach (var skeleton in skeletons) skeleton.Hide();
                current = null;
            }
        }
        void OnGUI()
        {
            if(App!=null)return;
            var title = new GUIStyle(GUI.skin.label) { fontSize = 23 };
            var text = new GUIStyle(GUI.skin.label) { fontSize = 17 };
            GUI.Label(new Rect(16, 8, Screen.width - 24, 34), "SignCoach Lite | TCP hand skeleton", title);
            GUI.Label(new Rect(16, 44, Screen.width - 24, 26), "127.0.0.1:5005  |  " + status, text);
            bool hasLeft = false, hasRight = false;
            if (current != null) foreach (var h in current.hands) { hasLeft |= h.handedness == "Left"; hasRight |= h.handedness == "Right"; }
            GUI.Label(new Rect(16, 72, Screen.width - 24, 25),
                string.Format("Hands: {0}/2   Left: {1}   Right: {2}   Receive: {3:F1} FPS   Render: {4:F1} FPS", VisibleHands,
                hasLeft ? "detected" : "--", hasRight ? "detected" : "--", connected ? receiveFps : 0, renderFps), text);
            GUI.Label(new Rect(16, 100, Screen.width - 24, 25), "Mirror view | Left: green | Right: blue | 21 points per hand | Esc: quit", text);
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape) Application.Quit();
        }
        void OnDisable()
        {
            if (client != null)
            {
                client.ValidFrameReceived -= Recording.Recorder.Receive;
                client.StreamInterrupted -= Recording.Recorder.Interrupt;
                client.Dispose();
                Debug.Log("SignCoach TCP stopped; worker alive: " + client.WorkerAlive);
                client = null;
            }
            if (skeletons != null) foreach (var skeleton in skeletons) skeleton.Hide();
            if (Recording != null) Recording.Recorder.Cancel();
        }
        void OnDestroy() { Destroy(left); Destroy(right); Destroy(unknown); Destroy(dot); }
    }
}
