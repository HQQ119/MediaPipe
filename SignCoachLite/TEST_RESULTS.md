# 第一阶段实际测试记录

测试日期：2026-11-09。工作目录：`D:\测试项目\unity\SignCoachLite`。
原有 Unity 学习资料和脚本未修改。本阶段所有文件均放在新增的 SignCoachLite 目录中。

## 环境与模型

- 本机存在 Python 3.11、3.12、3.14。默认 `python` 为 `D:\python\python.exe`，3.12.1。
- 初始默认环境没有 MediaPipe；装有 NumPy 2.2.6、opencv-python-headless 5.0.0.93。
- 新建隔离 `.venv`，安装 MediaPipe 1.2.2、NumPy 2.5.4、opencv-contrib-python 5.0.0.93。
- 实际导入 Tasks HandLandmarker 成功；该 MediaPipe 没有 `solutions`，未使用旧接口。
- OpenCV 构建实际报告 `GUI: WIN32UI`。`pip check` 通过，完整已安装版本见 `output/environment.txt`。
- 官方版本 1 float16 模型实际下载并通过 CPU 加载，文件 SHA256：
  `fbc2a30080c3c557093b5ddfc334698132eb341044ccee322ccf8bcf3607cde1`。
  此值是本次下载文件的指纹，非另行取得的官方签名。

## 已完成的真实运行

| 检查 | 结果及依据 |
| --- | --- |
| Python、依赖、语法 | 实际导入成功、compileall 通过、pip check 通过 |
| 默认摄像头打开和采集 | 索引 0 成功，先无窗口连续处理 30 帧，后实际 GUI 处理 20 帧 |
| 实时窗口与 FPS | cv2.imshow / waitKey 实际运行；20 帧试跑最终平滑 FPS 21.5，截图时 20.8 |
| 无手画面和数据 | GUI 20 帧均为空手结果，画面仍正常显示，JSON 的 hands 为 [] |
| 截图 | 实际 S 分支保存 `output/capture_1794219955722470900.png` 及同名 JSON，画面当时无人举手 |
| 单手识别 | 官方 thumb_up.jpg 实际模型推理得到 1 只手、21 点 |
| 两只手识别 | 官方 right_hands.jpg 实际模型推理得到 2 只手，各 21 点；这张图片是两只右手，不能证明真人一左一右已验收 |
| 模型标签与分类分数 | 官方图片产生真实 handedness 和 handedness_score，未按位置猜测 |
| 骨骼绘制 | 21 点、21 条官方连接已实现；真实模型图片绘制另存 official_two_hands_annotated.png |
| JSON 结构 | 空手、单手、双手结构往返序列化、21 个连续 id、原始 z、标签分数测试通过 |
| 失败处理 | 真实索引 99 无法打开时输出中文错误；模拟读取失败验证退出清理 |
| Q 退出 | 真实窗口处理 20 帧后，验证脚本自动提供 Q 键码，运行退出；人工键盘输入尚未验收 |
| 释放与重开 | 上述运行关闭后，真实摄像头再次打开并处理 5 帧成功；真实模型上下文关闭检查通过 |

摄像头记录：`output/camera_probe.jsonl`、`output/gui_probe.jsonl`、`output/reopen_probe.jsonl`。
第一次 30 帧采集的最终平滑 FPS 为 4.7，随后 GUI 试跑为 21.5；均是短时实测，不承诺长期固定帧率。
截图保留了真实摄像头中的个人画面，仅保存在本地；没有上传。

## 自动测试

执行 `.venv\Scripts\python.exe -m unittest -v test_pipeline`：**7 项全部通过、0 跳过**。
测试含纯数据/模拟故障，以及真实官方模型的空白帧、官方单手/双手图片推理。
具体日志见 `output/tests.log`，官方图片推理的 JSON 见 `output/thumb_up_result.json` 和 `output/right_hands_result.json`。
测试中的 mock 帧率没有性能意义，不计入上面的摄像头 FPS。

测试图片来自 Google 官方测试素材：

- https://storage.googleapis.com/mediapipe-assets/thumb_up.jpg
- https://storage.googleapis.com/mediapipe-assets/right_hands.jpg
- 官方测试引用：https://github.com/google-ai-edge/mediapipe/blob/master/mediapipe/tasks/python/test/vision/hand_landmarker_test.py

使用系统 curl 直连 Google 存储超时；Python urllib 使用本机网络配置下载成功。运行时模型已缓存，不再需要下载。

## 仍需人工完成的验收

- 本人左手、右手分别进入画面，核对模型标签对应实际手，检查摄像头驱动是否自身镜像。
- 真人双手同时进入、交换位置、移动和短暂遮挡时，检查骨骼贴合、左右标签和长期稳定性。
- 在窗口中实际按 Q，以及点击窗口关闭按钮的交互体验。
- 在用户实际光照和设备条件下观察持续帧率；其他摄像头、断开再连接未实测。

本程序只提取关键点，尚不能判断手语含义或评价动作。以上未验收项不计为通过。

## 后续真人配合测试（同日）

用户按顺序配合举左手、右手、双手，持续读取正在运行的 `output/live_validation.jsonl`：

- 左手阶段：记录到 Left，取样帧含 21 点，分类分数约 0.918。保存结果见 `output/left_hand_live_evidence.json`。
- 右手阶段：检查时最近 100 帧均为单手 Right；取样帧含 21 点，分类分数约 0.815。见 `output/right_hand_live_evidence.json`。
- 双手阶段：检查时最近 100 帧中，98 帧为 Left + Right，2 帧为 Right + Right，说明左右分类仍有短暂误判。
  最近一帧 Left + Right 各有 21 点，分类分数分别约 0.592、0.954。见 `output/both_hands_live_evidence.json`。
- 本次检查读到 1915 帧（末帧会话时间 131890 ms），其中累计 214 帧同时输出 Left + Right；
  该累计数包含举手和撤手过程，不是稳定性准确率。摘要见 `output/live_validation_summary.json`。

本轮已验证真人单手和双手的数据输出，更新前面对应的待验收状态；尚未得到本轮新截图，
没有把短时测试当作长期稳定性或人工 Q 退出已通过。上面的静态图片与首次无手测试记录仍保留。

### 移动配合后的检查

用户完成缓慢左右移动指令后，截取最后一个双手结果之前的 5 秒日志（239615–244615 ms）：
151 帧均检测到两只手，各帧每只手均为 21 点；149 帧为 Left + Right，1 帧 Left + Left，1 帧 Right + Right。
这是日志时间窗口，未用视频逐帧标注动作起止，不能当作严格同步的移动识别准确率。
完整等待区间包含撤手、无手和单手结果，没有把整个区间的无手帧直接认定为检测丢失。
证据见 `output/movement_recent_window.json` 和 `output/movement_validation_summary.json`。
本轮短时双手检测可用，左右分类仍偶有跳动，尚未增加平滑或修改模型输出标签。

### 人工退出与最终状态

用户回复已按 Q 后，原进程已结束，退出码 0，实际处理 5179 帧，退出时平滑 FPS 15.1。
最终 JSONL 有 5179 条完整记录，时间戳严格递增（211–312567 ms），所有已输出的手均含 21 个点。
随后再次打开默认摄像头，CPU 推理 5 帧并正常退出，验证设备可以重新获取。

退出时还读到本轮 S 截图的日志；真实截图为 `output/capture_1794220421032804800.png`，
同名 JSON 中是一只 Left、21 个点，分类分数约 0.892。该截图已实际查看，能看到真实左手及绿色骨骼。
之前“尚无本轮截图”的阶段性记录由此更新；并未保存真人双手画面的截图。

**最终验收状态：**真人单左手、单右手、双手同时识别、21 点数据、FPS、JSON、人工配合 Q 退出、资源释放与重新打开均已完成验证。
双手的左右标签存在少量短暂误判，不能声称完全稳定；长期运行、交叉遮挡、不同光照及不同设备仍待扩大测试。
点击窗口关闭按钮尚未单独测试。未增加动作评分、Unity 或网络功能。

## 最后小幅收尾（2026-11-09，覆盖前文关闭按钮待测状态）

修改 `renderer.py`，将状态框及四行状态文字移至右上角；保持原尺寸，并随画面宽度对齐右侧。
`main.py` 原有的 `waitKey` 后检查 `WND_PROP_VISIBLE` 已能处理本机窗口关闭，不需要改动。
模型文件、推理参数、左右标签逻辑、JSON 结构均未修改。
已与上一版审核包逐字节 SHA256 对比 main.py、camera.py、hand_detector.py、data_processor.py、config.py、requirements.txt 和模型，全部相同。

### 实际运行与关闭按钮

- 执行 `output/verify_window_close.py --json-output output/window_close_probe.jsonl`。
- 使用真实默认摄像头、真实 CPU 模型和 OpenCV Win32 窗口，处理 20 帧。
- 第 10 帧自动触发 S 保存真实截图；第 20 帧由 Windows 鼠标输入点击已找到并置前的本程序窗口标题栏关闭按钮，未用 Q 代替关闭。
- 点击后程序退出码为 0，循环没有重新创建窗口；检查到原生窗口已消失。
- 对真实 Camera/HandDetector 的 close 调用作记录，确认 capture 和 _landmarker 均已置为 None。
- 随后重新打开默认摄像头并推理 5 帧，正常结束，记录在 `output/after_window_close_reopen.jsonl`。
- 本轮实际摄像头画面没有手，不把本轮截图称为真人双手识别截图；模型识别回归由原有官方单手/双手图片推理测试验证。

真实截图：`output/capture_1794221336187735600.png`，右上状态框可见；截图时平滑 FPS 14.2，20 帧运行退出时 23.7。
原生鼠标点击、资源状态及退出码的结果见 `output/window_close_verification.json`。
此结果验证本机 Windows / WIN32UI 的行为，未声称其他操作系统或 OpenCV 后端都已实测。

### 自动回归测试

在 `test_pipeline.py` 新增两项：

- 状态框位置测试：640 和 1280 宽度下均在右上，左上画面保持可见，背景与文字一起移动，数据不被修改。
- 窗口关闭释放测试：模拟可见性变为 0，走真实资源包装类的 __exit__/close，断言底层摄像头 release、模型 close、窗口销毁均被调用。

执行 `.venv\Scripts\python.exe -m unittest discover -v`：**原有 7 项 + 新增 2 项，共 9 项全部通过，0 跳过**。
官方单手/双手图片和空白帧仍使用真实 CPU 模型推理。`pip check` 同时通过。
完整自动测试日志：`output/tests_final_cleanup.log`。
本轮没有重测真人双手动作和长期稳定性，原有左右标签短暂跳动问题未在本次调整中处理。
