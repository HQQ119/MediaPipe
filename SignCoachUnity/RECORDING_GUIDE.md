# 第三阶段：动作录制与本地保存

本工具录制 MediaPipe 的关键点时间序列。8 个词汇只是候选目录，**不判断手语正确性，也不自动认证标准动作**。
用户本轮没有可靠示范素材，交付的 hello 样本只表示选中了“你好”目录，内容是双手举起、移动、放下的采集测试。
所有保存 metadata 都标记 `verification_status: unverified_test`，没有标准动作评分、DTW 或示范认证功能。

## 启动

Python 保持第二阶段方式，不修改识别或协议：

```powershell
cd 'D:\测试项目\unity\SignCoachLite'
.\.venv\Scripts\python.exe main.py --tcp
```

然后双击 `Build/SignCoachLite.exe`，或 Unity 2022.3.62f3c1 中打开 `Assets/Scenes/SignCoachDebug.unity` 后 Play。
Unity 只接收 Python 的相机数据，不再打开第二个摄像头。

默认保存目录为 `Application.persistentDataPath/data/reference`，通常在 Windows 的
`%USERPROFILE%\AppData\LocalLow\SignCoachLite\SignCoach Lite\data\reference`，实际绝对路径会显示在录制面板。
开发机本次样本保存在项目内，启动时明确指定了目录：

```powershell
& 'D:\测试项目\unity\SignCoachUnity\Build\SignCoachLite.exe' `
  -recordingRoot 'D:\测试项目\unity\SignCoachUnity\data\reference'
```

数据不放在 Assets/Resources 内，不需要每录一次就导入 Unity。构建默认窗口 1200×820，右侧为录制面板，左侧继续实时骨骼显示。
面板使用 Windows Microsoft YaHei 动态字体显示中文；本次仅验证 Windows，其他平台中文字体需另测。

## 使用

1. 选择候选词汇，词汇 ID 与名称独立保存。可以填写示范来源；仅填写链接不会把录制变成已审核标准。
2. 收到实时有效帧后，点击“开始录制”。面板显示录制时长、帧数、当前手数。
3. 点击“停止录制”，校验完整文件。通过后点击“保存录制”；存在质量提示时须先勾选确认，才允许保存为未审核测试数据。
4. “取消录制”删除本次未保存的临时文件，不删除历史；“重新录制”取消当前草稿并开始新的 take，不覆盖已保存 take。
5. 历史中“读取并验证”重新解析整份 JSONL 并核对 metadata；显示读取帧数，不会用历史动作替换实时画面。
6. 删除先点击“删除…”，再点击“确认删除”。可以选择保留；删除为永久删除该 take 与独立 metadata，不删除其他 take。

正在录制或有草稿时不能切换词汇。关闭 Unity 会取消尚未保存的草稿，不会静默生成标准动作。
TCP 中断、无效帧或时间戳重置会把当前草稿标为无效，请取消并重录，不能拼接两个会话的时间戳。

候选词汇：

| word_id | word_name |
| --- | --- |
| hello | 你好 |
| goodbye | 再见 |
| thanks | 谢谢 |
| sorry | 对不起 |
| eat | 吃饭 |
| drink | 喝水 |
| help | 帮助 |
| like | 喜欢 |

## 文件结构

```text
data/reference/
  hello/
    metadata.json
    take_001.jsonl
    take_001.metadata.json
    take_002.jsonl
    take_002.metadata.json
```

每次录制有独立 `take_NNN.metadata.json`，含 word_id、word_name、take_id、created_at（UTC ISO8601）、frame_count、duration_ms、
source、coordinate_system、handedness_convention，以及来源说明、手帧/空手帧计数、首末时间戳、审核状态和质量提示。
词汇目录 `metadata.json` 的顶层是最新 take 摘要，`takes` 数组是全部录制的 metadata 索引。
历史列表按实际 `.jsonl` 与独立 metadata 扫描，不只依赖索引；索引失败会明确提示，已保存数据保留。

开始录制先用 CreateNew 预留新的 `.pending` 编号，保存后改名为 `.jsonl`。扫描现存 take 的最高编号后分配下一个，拒绝覆盖已存在文件。
取消的未保存编号或已删除的最高编号可能再次使用；不能将 take 编号当作跨删除历史的永久唯一身份。
意外崩溃留下的 `.pending` 不计作已保存录制，也不会当作标准数据自动加载。

## JSONL 与第四阶段读取

网络数据结构完全沿用第二阶段。只在**录制文件**顶层添加本阶段要求的 `frame_index`，从 0 连续递增：

```json
{"frame_index":0,"timestamp":123456,"hands":[]}
```

有手的每帧仍有 handedness、handedness_score、21 个 id/x/y/z。无手帧完整保留 `hands: []`，不伪造或插值。
接收线程直接复制原始 JSON 文本并添加索引，不经过显示层，也不重新序列化坐标，因此保留精度、null 分数、标签和点序。
`timestamp` 保留 Python 原始会话毫秒，不改成录制相对时间；`duration_ms = last_timestamp - first_timestamp`。
界面时长是录制墙钟时间，网络停顿时可能与文件中的传感器时间跨度不同，会给出质量提示。
x/y 为原始未镜像图像归一化坐标，y 向下，z 为 MediaPipe 相对深度。显示的镜像、Y 轴翻转只用于骨骼绘制。

Unity 直接读取：

```csharp
var store = new SignCoach.RecordingStore(referenceRoot);
var sample = store.Load("hello", "take_002");
// sample.metadata：来源、审核状态和时间范围。
// sample.frames：完整 RecordedFrame[]，每帧包含 timestamp/frame_index/hands。
```

也可用 Python 标准库直接读取实际样本（这只是读取，没有 DTW/评分）：

```python
import json
from pathlib import Path
folder = Path(r"D:\测试项目\unity\SignCoachUnity\data\reference\hello")
metadata = json.loads((folder / "take_002.metadata.json").read_text(encoding="utf-8"))
frames = [json.loads(line) for line in (folder / "take_002.jsonl").read_text(encoding="utf-8").splitlines()]
assert len(frames) == metadata["frame_count"]
assert [f["frame_index"] for f in frames] == list(range(len(frames)))
```

这两份真人样本可供第四阶段做文件读取和序列接口联调，**不能作为正确“你好”的标准示范或评分基准**。
要建立真实标准库，须由可靠素材/合格示范者提供动作，再按相同工具录制并人工审核来源与内容。
当前没有修改审核状态的自动按钮或认证流程。

## 保存前校验

- 至少 5 个有手帧，传感器时间跨度至少 2000ms；全空手和过短录制拒绝保存。
- 整份 JSONL 可解析，frame_index 连续、时间戳非负且严格递增，metadata 与帧数和首末时间一致。
- 每只手 21 个唯一 id 0–20，坐标有限；左右分类分数合法。
- 明显异常坐标（x/y 超过 -1–2、|z|>5）拒绝；轻微 x/y 超出 0–1 只提示并保留原值，不裁剪。
- 空手超过一半、双手同标签、相邻数据间隔 >500ms、停止前 >500ms 无新数据，会提示并要求确认。

这些是采集质量的工程阈值，不是手语正确性或动作评分。具体动作是否标准，必须人工结合可靠示范判断。
第二阶段的 Unknown/null 分数在现有 C# DTO 中仍按默认数值解析，但落盘原文保留 null；需要完整 nullable 语义时读取原始 JSONL。

## 自动测试

沿用 Unity README 的 `BuildAndVerify.Execute` 构建命令，同时执行 `RecordingTests.Run`。
录制断言报告在 `Verification/recording_tests.txt`，覆盖文件完整性、目录隔离、质量拒绝、取消、二次删除、读取以及真实 TCP 100 帧突发录制。
临时模拟数据只在专用临时目录测试并清理，不混入交付真人样本。实际摄像头结果见 RECORDING_TEST_RESULTS.md。
