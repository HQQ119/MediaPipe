# 第三阶段实际测试与交付记录

日期：2026-11-09；Unity 2022.3.62f3c1 / Windows64 Mono。所有文件在原工作区完成。
用户明确答复没有可靠示范素材，因此本轮样本统一为 **未审核采集测试数据**，不是标准“你好”手语，也没有实现认证或评分。

## 修改文件

- 新增 `Assets/Scripts/RecordingStore.cs`：8 词汇 ID/名称、本地存储、完整文件校验、读取、历史、二次删除、metadata 索引。
- 新增 `Assets/Scripts/ActionRecorder.cs`：线程安全录制、停止/取消、保存前校验、原始 JSON 与全部帧保留。
- 新增 `Assets/Scripts/RecordingPanel.cs`：中文录制面板、词汇选择、时长/帧数/手数、质量提示、保存与历史管理。
- 新增 `Assets/Editor/RecordingTests.cs`，修改 `BuildAndVerify.cs`：加入录制自动检查，调试窗口默认 1200×820。
- 修改 `HandTcpClient.cs`：在每个有效帧解析后发出独立录制事件；无效数据/断线发出中断事件。绘制的最新帧邮箱继续保留。
- 修改 `SignCoachDebug.cs`：接入录制面板，将骨骼视口放在左侧；原骨骼映射、颜色及左右标签未改。
- 修改 `VerificationCapture.cs`：仅在显式验收模式下记录录制状态、接受测试指令。正常运行不处理这些文件。
- 更新 README，新增 RECORDING_GUIDE.md、本记录；更新调试场景、生成对应 .meta 和 Windows 构建。
- 本轮验收脚本、日志和截图在 Python `output/stage3/`。它们是验收工具，不参与正常 TCP 协议。

Python 的 camera、hand_detector、data_processor、renderer、main、tcp_server、config、requirements 均与第二阶段交付包字节一致。
Unity 的 HandFrame.cs、HandSkeleton.cs 也逐字节一致。核对结果：`output/stage3/unchanged_protocol_and_vision.json`。
没有增加第三方依赖，没有加入 DTW、评分、错误定位、词汇示范生成或 AI 功能。

## 自动测试（最终源码）

- Python：`python -m unittest discover -v`，**14 项全部通过，0 跳过**，包括前两阶段原有识别与 TCP 测试。
- Unity：实际调用 `BuildAndVerify.Execute`，先运行 **34 条录制断言**，再运行原有协议、坐标、真实本机 TCP、重连和线程退出检查，最后构建 Windows Player。
- 34 条覆盖：词汇字典、完整序列、空手与双手、原始 z/x/时间戳、frame_index、连续多 take 不覆盖、metadata/索引、读取、取消、
  未确认不删除/确认删除、过短/全空手/断线/时间戳重置拒绝、质量提示需确认、异常索引/坐标/JSON 拒绝、路径约束、null 分数原文保留、静默尾部提示。
- 专门用真实本机 C# TCP 突发发送 100 个模拟帧，绘制侧完全不调用 Snapshot，录制仍保存并重新读取 100 帧。
  这说明录制独立于只保留最后一帧的绘制邮箱；不是只保存最后一帧。
- 模拟数据只进入带 GUID 的临时测试目录，测试后清理，未混入真人样本。
- 最终记录：`Verification/recording_tests.txt`、`Verification/editor_tests.txt`、Python `output/stage3/unity_build.log` 和 `python_regression.log`。

## 真实摄像头录制

实际启动一个 Python 摄像头/MediaPipe 实例、一个 Unity Player，用户按指令两次举双手、移动、撤手。
有手录制通过验收入口调用与界面相同的开始/停止/保存控制器；保存时明确确认质量提示，只标为未审核数据。
没有把该测试描述为录制了标准手语。

| 样本 | 总帧数 | 时间跨度 | 双手帧 | 单手帧 | 无手帧 |
| --- | --- | --- | --- | --- | --- |
| hello/take_001.jsonl | 551 | 25103 ms | 545 | 6 | 0 |
| hello/take_002.jsonl | 673 | 22336 ms | 599 | 4 | 70 |

两份的 frame_index 都从 0 连续到末帧，时间戳严格递增；每只检测到的手都是 21 个关键点。
与 Python 原始日志逐帧比较，去掉新增存储字段 frame_index 后，整个帧字典相同，包含标签、分数、全部 id/x/y/z 和空 hands。
在每份样本首末时间戳区间内，Python 原始帧分别也是 551、673，确认本次区间内没有漏帧。
这不是对任意负载网络都不丢帧的保证；录制保证保存 Unity 实际收到的有效帧，不会伪造没收到的帧。

质量提示也写入 metadata：轻微坐标越界、双手同标签均保留原值并提示，不悄悄裁剪或改标签。
第二次样本的 70 个无手帧真实保存，没有补造关键点。
两份原始文件 SHA256：

- take_001：`7bae8d55490c6543254625736ee054bd44de7379a81ba0831184d12f01af9f87`
- take_002：`2c5ec2a666aa3f8d6c2d435a374ff2663add881017dea2499fdb47e503a0fd38`

详细结果见 Python `output/stage3/real_samples_validation.json`。

## 管理与实际按钮

- 真实运行中先后重新读取 take_001（551 帧）和 take_002（673 帧），校验通过。
- 关闭并重开最终构建后，历史重新从文件扫描，两份仍存在；再次读取 take_002 成功，记录 `final_reload_state.json`。
- 实际运行中执行“重新录制”再“取消”，临时文件删除，历史两份及哈希保持不变；最终没有遗留 `.pending`。
- 请求删除 take_001 后保留，未经确认文件未删除；真实样本均未永久删除。永久删除选择性与二次确认由临时目录自动测试验证。
- 最终构建还通过真实 Windows 鼠标点击：选择“再见”、开始录制、停止录制、取消录制。
  这次摄像头没有手，收到 1076 个空手帧、录制墙钟约 36.083 秒，停止后明确显示“有效有手帧少于 5 帧；不能保存为空动作”。
  没有生成 goodbye 的正式 JSONL；取消后可切回 hello 并重新读取历史。
- 用户做完动作后回复“已完成”；没有要求用户将测试动作当作候选词汇的示范。

最终 UI 字体/换行和静默尾部提示调整后，重新构建、重跑全部 Unity 检查，并做上述实际按钮空手测试与历史重新读取。
两份有手样本摄于这些显示/提示调整之前，帧存储和原始数据未改；没有声称最终构建重新录了另一轮标准手语。
整个 Python 会话处理 19458 帧后正常退出，Unity 最终日志显示 `worker alive: False`，未出现 Exception。

## 截图与证据

Python `output/stage3/`：

- `recording_take_1.png`、`recording_take_2.png`：真实双手、Unity 骨骼和正在增长的录制帧数。
- `saved_take_1.png`、`saved_take_2.png`：真实保存结果。
- `final_ready.png`、`final_loaded_sample.png`：最终构建、两份历史及读取成功。
- `no_hands_recording_rejected.png`：实际鼠标操作后，全空手录制被拒绝保存。
- `delete_confirmation.png`：早期构建的请求删除画面，最终 UI 另有自动滚动至确认区的调整；真实样本未删除。
- `camera_session.jsonl`：完整真人摄像头会话；`real_run/unity_applied.jsonl`：绘制应用帧，不能替代完整录制文件。
- `player.log`、`final_player.log`、`real_recording_probe_result.json`、`real_samples_validation.json`、`final_reload_state.json`。

截图有本人真实画面，仅保存在本地，没有自动上传。没有生成或伪造运行截图。

## 还需要人工验证

- **未提供已认证标准手语**：用户没有可靠素材，本轮只交付格式合规的真人测试样本；第四阶段可用来联调读取，不能用来给学习者评分。
- 各候选词汇的正确动作、示范来源、教师审核尚未进行。
- 长时间录制、大量历史文件、极低磁盘空间/权限故障、突然断电、不同操作系统字体及显示缩放尚未完整实测。
- 真实样本的永久删除确认、本轮有手样本质量提示勾选及保存的逐按钮人工操作尚未由用户逐项验收；保存控制器和文件删除逻辑已有自动测试/验收入口验证。
- 原有左右分类跳动仍保留；没有增加纠正、平滑、插值或评分。

完整运行、目录、校验阈值和第四阶段读取示例见 RECORDING_GUIDE.md。
