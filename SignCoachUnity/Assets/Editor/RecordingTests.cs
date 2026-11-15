using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using SignCoach;

public static class RecordingTests
{
    static int checks;
    static void Check(bool ok, string label) { if (!ok) throw new Exception("Recording test failed: " + label); checks++; }
    static void Reject(Action action, string label)
    {
        bool rejected = false; try { action(); } catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex is IOException) { rejected = true; }
        Check(rejected, label);
    }
    static string Frame(int i, bool empty = false, int hands = 2)
    {
        var data = new HandFrame { timestamp = 1000 + i * 100, hands = new HandData[empty ? 0 : hands] };
        for (int h = 0; h < data.hands.Length; h++)
        {
            var hand = new HandData { handedness = h == 0 ? "Left" : "Right", handedness_score = .9375f, landmarks = new Landmark[21] };
            for (int j = 0; j < 21; j++) hand.landmarks[j] = new Landmark { id = j, x = .25f + j * .01f, y = .3f, z = -.01f * j };
            data.hands[h] = hand;
        }
        return JsonUtility.ToJson(data);
    }
    static void Feed(ActionRecorder recorder, int count = 31, bool onlyEmpty = false)
    {
        for (int i = 0; i < count; i++) { var raw = Frame(i, onlyEmpty || i % 10 == 0); recorder.Receive(HandProtocol.Parse(raw), raw); }
    }
    public static void Run()
    {
        checks = 0;
        var root = Path.Combine(Path.GetTempPath(), "SignCoachRecordingTests_" + Guid.NewGuid().ToString("N"));
        var store = new RecordingStore(root);
        try
        {
            Check(Vocabulary.Words.Length == 6 && Vocabulary.GetSelectable("thanks").name == "谢谢", "six selectable vocabulary IDs and names");
            Reject(() => Vocabulary.GetSelectable("hello"), "removed hello cannot be selected");
            Reject(() => Vocabulary.GetSelectable("drink"), "removed drink cannot be selected");
            Check(Vocabulary.Get("hello").name == "你好", "old recordings remain readable for algorithm regression");
            using (var recorder = new ActionRecorder(store))
            {
                Reject(() => recorder.Start("hello", "removed"), "removed hello cannot start a new recording");
                Reject(() => recorder.Start("drink", "removed"), "removed drink cannot start a new recording");
                recorder.Start("goodbye", "synthetic automated test; NOT reference sign"); Feed(recorder);
                Check(recorder.Snapshot().frames == 31, "full sequence buffered");
                var validation = recorder.Stop(); Check(validation.IsValid, "valid sequence");
                var first = recorder.Save(false); Check(first.take_id == "take_001" && first.frame_count == 31 && first.duration_ms == 3000, "metadata and first take");
                var loaded = store.Load("goodbye", first.take_id);
                Check(loaded.frames.Length == 31 && loaded.frames[0].hands.Length == 0 && loaded.frames[1].hands.Length == 2, "empty and two hands retained");
                Check(loaded.frames[1].hands[1].landmarks.Length == 21 && Math.Abs(loaded.frames[1].hands[1].landmarks[20].z + .2f) < 1e-6f, "all points original depth");
                Check(loaded.frames[1].hands[0].landmarks[0].x == .25f && loaded.frames[1].timestamp == 1100 && loaded.frames[30].frame_index == 30, "no mirror/no timestamp rewrite/indexes");
                var before = File.ReadAllText(store.FramePath("goodbye", first.take_id));
                recorder.Start("goodbye", "test"); Feed(recorder); recorder.Stop(); var second = recorder.Save(false);
                Check(second.take_id == "take_002" && File.ReadAllText(store.FramePath("goodbye", first.take_id)) == before, "no overwrite across takes");
                Check(store.List("goodbye").Count == 2, "history reload");
                var catalog = JsonUtility.FromJson<WordCatalog>(File.ReadAllText(Path.Combine(root,"goodbye/metadata.json")));
                Check(catalog.takes.Length == 2 && catalog.take_id == "take_002" && catalog.source != null, "word catalog and take metadata");
                Reject(() => store.Delete("goodbye", first.take_id, false), "delete requires confirmation");
                Check(File.Exists(store.FramePath("goodbye", first.take_id)), "unconfirmed delete retains data");
                store.Delete("goodbye", second.take_id, true); Check(store.List("goodbye").Count == 1, "confirmed delete only selected take");
                recorder.Start("goodbye", "test"); Feed(recorder); recorder.Cancel();
                Check(!recorder.Snapshot().hasDraft && store.List("goodbye").Count == 1 && Directory.GetFiles(Path.Combine(root,"goodbye"),"*.pending").Length == 0, "cancel removes draft only");
                recorder.Start("goodbye", "test"); Feed(recorder, 5); Check(!recorder.Stop().IsValid, "short recording rejected");
                Reject(() => recorder.Save(true), "short cannot save"); recorder.Cancel();
                recorder.Start("goodbye", "test"); Feed(recorder, 31, true); Check(!recorder.Stop().IsValid, "all no-hands rejected"); recorder.Cancel();
                recorder.Start("goodbye", "test"); Feed(recorder); recorder.Interrupt("test disconnect");
                Check(!recorder.Stop().IsValid, "disconnect invalidates draft"); Reject(() => recorder.Save(true), "interrupted cannot save"); recorder.Cancel();
                recorder.Start("goodbye", "test"); Feed(recorder); string repeated = Frame(0); recorder.Receive(HandProtocol.Parse(repeated), repeated);
                Check(!recorder.Stop().IsValid, "timestamp reset rejected"); recorder.Cancel();
                recorder.Start("goodbye", "test");
                for (int i=0;i<31;i++) { var raw=Frame(i, i<23); recorder.Receive(HandProtocol.Parse(raw),raw); }
                var warned=recorder.Stop(); Check(warned.IsValid && warned.warnings.Count>0,"quality warning surfaced");
                Reject(() => recorder.Save(false), "warnings require acknowledgment"); recorder.Save(true);
                var corrupted = before.Replace("\"frame_index\":1,", "\"frame_index\":99,");
                var corruptPath=Path.Combine(root,"corrupt.jsonl"); File.WriteAllText(corruptPath,corrupted);
                RecordedFrame[] ignored; Check(!RecordingStore.Validate(corruptPath,false,out ignored).IsValid,"bad frame index rejected");
                File.WriteAllText(corruptPath,before.Replace("\"x\":0.25", "\"x\":20.25"));
                Check(!RecordingStore.Validate(corruptPath,false,out ignored).IsValid,"absurd coordinates rejected");
                Reject(()=>store.List("../escape"),"word path traversal rejected");
                // 原始 null 分数、精度文本必须不因重新序列化而改变。
                recorder.Start("thanks", "test");
                for(int i=0;i<31;i++) { var raw=Frame(i,false,1).Replace("\"handedness\":\"Left\"","\"handedness\":\"Unknown\"").Replace("\"handedness_score\":0.9375","\"handedness_score\":null"); recorder.Receive(HandProtocol.Parse(raw),raw); }
                Check(recorder.Stop().IsValid,"unknown null score parsed"); var nullable=recorder.Save(false);
                Check(File.ReadAllText(store.FramePath("thanks",nullable.take_id)).Contains("\"handedness_score\":null"),"null score raw JSON preserved");
                recorder.Start("help", "test"); Feed(recorder); Thread.Sleep(550);
                var stale = recorder.Stop(); Check(stale.IsValid && stale.warnings.Count > 0, "silent recording tail warns");
                Reject(() => recorder.Save(false), "tail warning requires acknowledgment"); recorder.Save(true);
                File.WriteAllText(corruptPath, "{bad json\n");
                Check(!RecordingStore.Validate(corruptPath,false,out ignored).IsValid,"malformed JSONL rejected");
            }
            // 接收线程批量收包而绘制根本未调用 Snapshot 时，录制仍保留每一帧。
            var listener = new TcpListener(IPAddress.Loopback,0); listener.Start();
            var client = new HandTcpClient("127.0.0.1",((IPEndPoint)listener.LocalEndpoint).Port);
            using(var recorder=new ActionRecorder(store))
            {
                recorder.Start("goodbye","synthetic TCP test"); client.ValidFrameReceived += recorder.Receive;
                try
                {
                    client.Start(); var accepted=listener.AcceptTcpClientAsync(); Check(accepted.Wait(3000),"recorder TCP connects");
                    using(var sender=accepted.Result)
                    {
                        var raw=new StringBuilder(); for(int i=0;i<100;i++) raw.Append(Frame(i,i%10==0)).Append('\n');
                        var bytes=Encoding.UTF8.GetBytes(raw.ToString()); sender.GetStream().Write(bytes,0,bytes.Length);
                        var timeout=DateTime.UtcNow.AddSeconds(4);
                        while(recorder.Snapshot().frames<100 && DateTime.UtcNow<timeout) Thread.Sleep(10);
                        Check(recorder.Snapshot().frames==100,"all 100 received frames recorded without render polling");
                        Check(recorder.Stop().IsValid,"100 frames validate"); var metadata=recorder.Save(false);
                        Check(store.Load("goodbye",metadata.take_id).frames.Length==100,"100 frames reload");
                    }
                }
                finally { client.ValidFrameReceived-=recorder.Receive; client.Dispose();listener.Stop(); }
            }
        }
        finally
        {
            // 仅清理本测试创建且前缀与绝对路径均已检查的临时目录。
            var full=Path.GetFullPath(root); var temp=Path.GetFullPath(Path.GetTempPath());
            if(full.StartsWith(temp,StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("SignCoachRecordingTests_")) Directory.Delete(full,true);
        }
        Directory.CreateDirectory("Verification"); File.WriteAllText("Verification/recording_tests.txt","PASS: "+checks+" recording assertions; synthetic fixtures, real TCP burst test; no real-camera claim\n");
    }
}
