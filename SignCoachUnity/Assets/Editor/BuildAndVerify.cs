using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEditor.Build.Reporting;
using SignCoach;

public static class BuildAndVerify
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Execute()
    {
        RecordingTests.Run();
        ComparisonTests.Run();
        ComparisonRobustnessTests.Run();
        ScoringProfileTests.Run();
        TeachingTests.Run();
        var empty = HandProtocol.Parse("{\"timestamp\":1,\"hands\":[]}"); Check(empty.hands.Length == 0, "empty frame");
        var lines = new JsonLineBuffer(); var messages = new List<string>();
        var bytes = Encoding.UTF8.GetBytes("{\"timestamp\":1,\"hands\":[]}\n{\"timestamp\":2,\"hands\":[]}\n");
        lines.Feed(bytes, 5, messages.Add); var rest = new byte[bytes.Length - 5]; Array.Copy(bytes, 5, rest, 0, rest.Length);
        lines.Feed(rest, rest.Length, messages.Add); Check(messages.Count == 2, "TCP split/coalesced lines");
        bool bad = false; try { HandProtocol.Parse("{\"timestamp\":2,\"hands\":[{\"handedness\":\"Left\",\"landmarks\":[]}]}"); } catch (FormatException) { bad = true; }
        Check(bad, "bad landmark count rejected");
        Check(HandProtocol.Position(new Landmark { x = 0, y = 0 }) == new Vector3(3.2f, 2.4f, 0), "top left mirror/Y map");
        Check(HandProtocol.Position(new Landmark { x = 1, y = 1 }) == new Vector3(-3.2f, -2.4f, 0), "bottom right mirror/Y map");
        Check(HandSkeleton.Edges.GetLength(0) == 21, "bone count");
        // 实际 C# TcpClient 接收测试：模拟源仅用于协议/线程验证，不计真人验收。
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var network = new HandTcpClient("127.0.0.1", port);
        try
        {
            network.Start();
            var accept = listener.AcceptTcpClientAsync(); Check(accept.Wait(3000), "client connects");
            using (var sender = accept.Result)
            {
                var stream = sender.GetStream();
                stream.Write(bytes, 0, 5); stream.Write(bytes, 5, bytes.Length - 5);
                var timeout = DateTime.UtcNow.AddSeconds(3);
                HandFrame received; long version; bool active; string message;
                do { network.Snapshot(out received, out version, out active, out message); Thread.Sleep(10); }
                while ((received == null || received.timestamp != 2) && DateTime.UtcNow < timeout);
                Check(received != null && received.timestamp == 2 && network.ReceivedFrames == 2, "actual stream split/coalesced receive");
            }
            var reaccept = listener.AcceptTcpClientAsync(); Check(reaccept.Wait(4000), "client reconnects");
            reaccept.Result.Close();
        }
        finally { network.Dispose(); network.Dispose(); listener.Stop(); }
        Check(!network.WorkerAlive, "worker exits and repeat dispose safe");
        // 回归连接尚在进行时退出的竞态，既覆盖已连接也覆盖拒绝连接路径。
        for (int i = 0; i < 20; i++)
        {
            var cancelled = new HandTcpClient("127.0.0.1", port);
            cancelled.Start(); Thread.Sleep(i % 3); cancelled.Dispose();
            Check(!cancelled.WorkerAlive, "cancel pending connection stops worker");
        }
        Directory.CreateDirectory("Verification"); File.WriteAllText("Verification/editor_tests.txt", "PASS: empty JSON, split/coalesced JSONL, invalid count, mirror/Y mapping, 21 bones, actual C# TCP receive/reconnect, worker disposal\n");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("SignCoach Debug").AddComponent<SignCoachDebug>();
        Directory.CreateDirectory("Assets/Scenes"); EditorSceneManager.SaveScene(scene, "Assets/Scenes/SignCoachDebug.unity");
        PlayerSettings.companyName = "SignCoachLite"; PlayerSettings.productName = "SignCoach Lite";
        PlayerSettings.defaultScreenWidth = 1200; PlayerSettings.defaultScreenHeight = 820;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed; PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        // 显式保留绘制使用的着色器，避免空场景构建时被剔除。
        var shader = Shader.Find("Unlit/Color");
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        var shaders = settings.FindProperty("m_AlwaysIncludedShaders");
        int n = shaders.arraySize; shaders.InsertArrayElementAtIndex(n); shaders.GetArrayElementAtIndex(n).objectReferenceValue = shader; settings.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        string destination="Build/SignCoachLite.exe";var arguments=Environment.GetCommandLineArgs();int outputIndex=Array.IndexOf(arguments,"-signcoachBuildPath");
        if(outputIndex>=0&&outputIndex+1<arguments.Length)destination=arguments[outputIndex+1];
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { "Assets/Scenes/SignCoachDebug.unity" },
            locationPathName = destination, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Unity build failed: " + report.summary.result);
        Debug.Log("SIGNCOACH_BUILD_AND_TESTS_PASSED");
    }
}
