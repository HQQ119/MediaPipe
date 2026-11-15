# SignCoach Lite 第二阶段测试与交付

日期：2026-11-09。Python：`D:\测试项目\unity\SignCoachLite`；独立 Unity 项目：`D:\测试项目\unity\SignCoachUnity`。
Unity 2022.3.62f3c1，Windows64 Mono 开发构建，构建产物 `SignCoachUnity/Build/SignCoachLite.exe`。
没有修改已有 IK 项目，没有加入评分、DTW、词汇库、账号、AI 或云端服务。

## 修改和新增

- Python 修改 main.py：增加 `--tcp`、`--host`、`--port` 选项和可选服务上下文，每次识别后发送原有帧数据。
- 新增 tcp_server.py：标准库非阻塞 TCP JSONL，单客户端、断线重连和有界发送缓冲。
- 新增 test_tcp_server.py：5 项网络/生命周期测试；第一阶段 test_pipeline.py 的 9 项测试保留。
- 更新 README，新增 STAGE2_PROTOCOL.md 和本测试记录。
- 新建 SignCoachUnity，包含全部 Assets、Packages、ProjectSettings、调试场景和构建/自动检查脚本。
- Unity 脚本：HandFrame、JsonLineBuffer、HandTcpClient、HandSkeleton、SignCoachDebug；VerificationCapture 为显式启用的验收记录工具。
- 相机、模型、推理参数、第一阶段 JSON 处理未改；已与第一阶段审核包核对 camera.py、hand_detector.py、data_processor.py、config.py、requirements.txt、模型文件的字节内容，全部一致。
  结果见 `output/stage2/unchanged_vision_files.json`。

## 已通过

| 验收项 | 实际依据 |
| --- | --- |
| 第一阶段回归 | 原有 9 项全部通过，含真实官方模型单手/双手图片和空白帧推理 |
| Python TCP 与非阻塞 | 新增 5 项测试通过：无客户端不阻塞，真实本机连接/断线重连，半包发送边界，慢客户端断开，主循环资源生命周期 |
| Python 不重复开相机/模型 | 主循环每个资源只创建一次；TCP 主循环测试覆盖多帧发送与相机初始化失败后的服务关闭 |
| Unity 编译与独立运行 | 实际 Editor 批处理构建成功，执行文件运行；没有依赖现有 IK 项目 |
| C# 自动协议和线程检查 | 实际运行 JSON、粘包/拆包、异常点数、镜像/Y 翻转、21 条骨骼、真实 TcpClient 收包/重连/线程退出、20 次连接取消退出检查 |
| Python 未启动时 Unity | 实际界面保持响应并提示 Connecting/retrying，骨骼为空；断线截图也记录同一重试状态 |
| 真实摄像头联合通信 | 用户配合举左手、右手、双手并移动、撤手，Python 与真实 Unity Player 同时运行，保存双窗口截图和两端日志 |
| 单手/双手骨骼 | 真实截图分别显示单绿色、单蓝色和两套骨骼；每只手 21 点、21 条骨骼，颜色读取模型标签，不依赖数组顺序 |
| 数据与坐标对应 | Python 4424 帧；Unity 应用 3895 帧，全部 3895 个时间戳可回查原始日志，标签、点 id、数量与坐标逐项一致 |
| 镜像与 Y | JSON 坐标未镜像；Unity 使用 `(0.5-x)*6.4, (0.5-y)*4.8`；已用静态边界坐标和真实双窗口截图核对显示方向 |
| 手部消失 | 真人撤手后 Unity 状态 visible=0，有撤手截图；真实 Player 回放检查还验证空 hands、坏数据和超时的清除 |
| 真实 Python 重启后的重连 | 不重启 Unity，重新打开摄像头服务 60 帧；Unity 自动收到全部 60 帧，接受新会话较小时间戳 |
| 断线与异常消息 | 实际 Player 接收拆分 JSON、坏点数、不完整行，仍能恢复；断线清除、重连恢复、超时清除均通过专门回放检查 |
| 最终构建关闭资源 | 修复退出竞态后，真实相机再跑 240 帧，Unity 应用 206 帧后退出；线程停止，Python 继续处理余下帧并正常结束 |

Python 最终命令：`.venv\Scripts\python.exe -m unittest discover -v`，**14 项全部通过，0 跳过**；`pip check` 通过。
C# 自动检查由 `BuildAndVerify.Execute` 在实际 Unity Editor 中执行，不是未编译的代码审阅；摘要在 `SignCoachUnity/Verification/editor_tests.txt`。
异常 Player 检查为回放数据，见 `protocol_player_checks.json`，不能替代上表另外列出的真人测试。

## 真人数据的具体范围

联合会话 Python 有 4424 帧，Unity 只开始接收服务启动后的部分帧，应用 3895 帧。
这不是所有 Python 帧必须逐帧渲染的丢帧率；无客户端时 Python 照常采集，Unity 主线程仅应用最新帧。
逐项比较后的最大坐标差为 0（本次 MediaPipe float 坐标在 C# JSON 往返后仍相同）。

Unity 应用帧的类别分布：无手 1823、单 Left 541、单 Right 249、Left+Right 1149、Left+Left 113、Right+Right 20。
混有举手、撤手和动作转换，未逐帧标注真实标签；**不能把这些分布当左右手识别准确率**。
重复左右标签是模型原输出，Unity 未强制改成一左一右，也没有增加平滑。

截图 `joint_left.png`、`joint_right.png` 为真人单手；捕获时 Windows 缩放导致 Python 窗口部分画面被裁切，Unity 骨骼仍完整可见。
之后修正的是验收截图工具的窗口尺寸，`joint_both.png` 和 `joint_hands_removed.png` 显示完整的 Python 画面。
截图含本人真实画面，只保存在本地，没有自动上传。

用户回复“已完成”确认做完动作，未逐项提供肉眼位置/延迟评价。双手截图已核对同方向、颜色和空间位置；
移动时有连续坐标和主线程应用记录，但未录制视频或测量端到端延迟，不声称逐帧视觉跟随精度已经量化验收。

## 本轮发现并修复的问题

1. 部分视口渲染时，视口外 FPS 文字未清除而叠印：新增只清背景、不绘制对象的全窗口 Camera，状态文字恢复正常。
2. Mono 的 TcpClient.Close 与 EndConnect 并发时可能因内部 Client 变 null 抛空引用：线程改为持有稳定的底层 Socket 引用，
   连接、结束连接、接收均用该引用，关闭后按 Socket/ObjectDisposed 异常正常退出。补连接取消回归和实际 Player 退出检查。
3. 验收状态文件原先每 30 帧刷新，可能错过仅持续 0.5 秒的回放画面：验证工具在可见手数变化时立即刷新，未改产品数据。

`live_player.log` 保留了修复前退出时的异常，不能拿它声称修复后无异常。
最终证据为 `final_player.log`、`final_build_validation.json` 和通过后的 `protocol_player_checks.json`：线程已停，无 Exception。
真人举手截图摄于上述 Socket 退出竞态修复之前；修复只涉及连接/退出，不改解析坐标或绘制。
修复后已重新构建、重跑自动检查、实际 Player 异常检查和真实摄像头 240 帧联合运行；该 240 帧画面没有手，未声称重做了整轮真人举手。

## 未通过或尚未完整验收

- **尚未完全稳定：**模型原有左右分类跳动仍存在，双手可能短暂同为 Left/Right。没有强制纠正，也不属于网络坐标交换。
- **待人工补测：**长时间真人移动跟随、主观延迟、交叉遮挡、不同光照与设备、热插拔、Editor 实际停止 Play、Windows 标题栏关闭 Unity 的操作。
- 当前 Unity 不显示摄像头视频背景，只显示匹配摄像头坐标的骨骼；请并排对照两个窗口。
- 没有本阶段计划内但完全无法运行的已知通信/绘制阻断项；未测试项不计通过。

## 证据索引

均位于 Python `output/stage2/`，除注明外：

- `python_tests.log`：最终 14 项结果；`unity_build.log`：最终编译及 Editor 检查日志。
- `real_joint_validation.json`、`live_camera.jsonl`、`live_joint_first_session.jsonl`：首次真人会话比对和原始数据。
- `joint_left.png`、`joint_right.png`、`joint_both.png`、`joint_hands_removed.png`：真实双窗口截图。
- `live_joint/unity_snapshot_11819.png`：Python 停止后的 Unity 重试界面。
- `reconnect_validation.json`、`camera_reconnect.jsonl`：真实重启自动重连。
- `protocol_player_checks.json`：最终实际 Player 的 8 项回放/异常检查。
- `final_camera.log`、`final_camera.jsonl`、`final_player.log`、`final_build_validation.json`：最终构建真实摄像头与退出证据。
- `capture_joint.py`、`watch_gestures.py`、`verify_unity_protocol.py`：本轮本地验收工具。

正常启动和协议见 README、STAGE2_PROTOCOL.md、Unity 项目 README；构建的 exe 是运行产物，完整可编辑源码在 Unity Assets/Packages/ProjectSettings 中。
