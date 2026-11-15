# SignCoach Lite · Python 识别端（第一、二阶段）

教学升级增加可选 `--preview-file` 本机摄像头背景输出和 `extract_teaching_video.py` 离线提取入口；
识别器、模型阈值、左右手标签、TCP及每帧数据结构保持原样。详见相邻Unity项目的 `TEACHING_GUIDE.md`。

Python + MediaPipe Tasks Hand Landmarker + OpenCV，最多两只手、每只 21 个关键点。
使用 CPU、同步 VIDEO 推理（包含相邻帧追踪），每帧结果与输入帧一一对应。
默认仍为第一阶段独立摄像头识别；第二阶段可用 `--tcp` 将原有 JSONL 发送给旁边的独立 SignCoachUnity 项目。
本阶段不包含评分或账号功能。通信与坐标说明见 [STAGE2_PROTOCOL.md](STAGE2_PROTOCOL.md)，测试见 [STAGE2_TEST_RESULTS.md](STAGE2_TEST_RESULTS.md)。

```powershell
.\.venv\Scripts\python.exe main.py --tcp
```

默认监听 127.0.0.1:5005，无 Unity 客户端也继续识别；客户端断线后等待重连。可用 `--host`、`--port` 配置地址。
不用 `--tcp` 时保留原有功能，模型、推理阈值、左右标签和 JSON 结构未修改。

## Windows 安装

已在本机 Python 3.12.1 64 位虚拟环境运行。不要使用 `py` 默认选择的 Python 3.14；以下显式选择 3.12。
PowerShell：

```powershell
cd 'D:\测试项目\unity\SignCoachLite'
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
.\.venv\Scripts\python.exe -m pip check
.\.venv\Scripts\python.exe main.py --download-model
.\.venv\Scripts\python.exe main.py
```

当前目录下 `.venv` 和模型已经准备好，可以直接执行最后一条。不需要激活环境或修改 PowerShell 执行策略。
MediaPipe 1.2.2 使用 Tasks API，本机该版本没有 `solutions`。OpenCV 使用 MediaPipe 依赖的
`opencv-contrib-python`（包含普通 OpenCV 和 GUI），避免同时安装多个提供 `cv2` 的包。
不要在这个环境再安装 `opencv-python-headless`。

模型不存在时会自动从 Google 官方存储下载到 `models/hand_landmarker.task`，下载到临时文件、检查模型包后再替换。
首次下载需要网络；之后可离线运行。自定义模型路径：`--model 'C:\models\hand_landmarker.task'`。
已有模型加载失败会显示错误并退出，不会悄悄换成其他模型。

网络无法访问时，浏览器打开[官方模型文件](https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task)，
保存到上述位置（需要先创建 `models` 文件夹）。不要把下载的 HTML 错误页面改名为 `.task`。
模型来源及接口说明：[MediaPipe 官方 Hand Landmarker 文档](https://developers.google.com/edge/mediapipe/solutions/vision/hand_landmarker?tab=Python)。

## 使用

```powershell
# 默认摄像头 0；默认请求 640×480，实际分辨率由设备决定
.\.venv\Scripts\python.exe main.py
# 切换摄像头；Windows 默认先尝试 DirectShow，再尝试 MSMF
.\.venv\Scripts\python.exe main.py --camera 1
.\.venv\Scripts\python.exe main.py --backend msmf
# 保存每帧识别结果，每行一个 JSON；会覆盖指定文件，默认不记录
.\.venv\Scripts\python.exe main.py --json-output output\session.jsonl
# 不开窗口的有限帧采集
.\.venv\Scripts\python.exe main.py --no-display --max-frames 100
```

窗口获得焦点后，Q / Esc 退出；S 保存当前真实画面的 PNG 和对应 JSON 到 `output/`。
也可关闭窗口或在终端按 Ctrl+C。正常退出、读取失败、模型异常均通过上下文管理器释放摄像头和模型，关闭窗口。
不要直接强制杀进程来测试资源清理。

画面镜像显示；Left 为绿色，Right 为蓝橙色。右上角显示手数、左右手检测状态与平滑 FPS。
为了兼容 OpenCV 内置字体，画面文字使用英文（`Place hands in camera view` 表示请把手放入画面）；终端错误和说明使用中文。
没有手时继续显示摄像头画面，输出 `hands: []`。读取不到视频或推理异常会明确报错并退出，避免持续发送过期结果。

FPS 是整个采集/推理循环的指数平滑值（包括上一帧绘制、等待和可选文件写入），不是单独的模型速度。
首次启动、曝光和设备设置可能影响帧率。靠近摄像头、逆光、遮挡、快速运动均可能降低识别效果。

## 验证双手

1. 在光照充足、背景简单的地方运行程序，先让画面中没有手：应为 `Hands: 0 / 2`。
2. 单独举左手，再单独举右手。检查实际手与模型标签是否相符，手上是否有 21 点和完整连接。
3. 双手同时张开放入画面，两手保持距离。应出现两套骨骼，`Hands: 2 / 2`，左右状态都显示 detected。
4. 交换双手在画面中的位置，确认标签不会单纯因位置互换；允许遮挡或低置信度时短暂误判。
5. 按 S 保存证据。打开对应 JSON，核对每只手的 landmarks 长度是 21、id 为 0–20，时间戳递增；撤手后 hands 为空。
6. 按 Q 退出，再运行一次，确认设备可重新打开。

模型标签直接取实际输出，不按画面位置猜测、不强行保证两只手一定各为 Left/Right。
按当前 Tasks 官方说明，推理使用原始未镜像画面以保留受试者左右手语义；显示时只将 x 映射为 `1-x`。
如果摄像头驱动自身已做镜像，须先检查驱动设置；真人左右手语义仍需按以上步骤验收。

## 数据接口（供第二阶段调用）

```python
from hand_detector import HandDetector
from data_processor import process_result, to_json

# detector 在循环外建立；frame 为 OpenCV 读到的原始 BGR ndarray。
with HandDetector() as detector:
    result = detector.detect(frame, timestamp_ms)  # 严格递增的整数毫秒
    frame_data = process_result(result, timestamp_ms)
    payload = to_json(frame_data)                 # str；后续可以 encode('utf-8')
```

上述片段中的 `frame` 和 `timestamp_ms` 由调用方提供，完整采集循环见 `main.py`。

顶层字段：`timestamp` 为会话起点后的单调毫秒（不是 Unix 时间），`hands` 为当前帧的数组，无手时为空。
每只手含 `handedness`、`handedness_score` 和 `landmarks`。示意结构：

```json
{"timestamp":123,"hands":[]}
```

有手时，每只手严格含 21 个 `{ "id": 0, "x": 0.5, "y": 0.5, "z": 0.0 }` 形式的点，id 连续 0–20。
`x/y` 相对于**原始未镜像**图像宽高归一化，左上角为原点，x 向右、y 向下；镜像显示时 x 使用 `1-x`。
保留模型原始浮点数，不舍入、不裁剪；边缘预测偶尔可能超出 0–1。
`z` 保留模型以手腕为原点的相对深度，不是米或真实距离，较小值表示更靠近摄像头。
`handedness_score` 是左右手**分类**分数，不能作为每点检测置信度。本 API 不提供逐点检测分数，因此不虚构该字段。
类别缺失时为 `Unknown`，分数为 null。数组顺序不是稳定的手部追踪 ID，后续不能把数组索引当身份。
JSONL 文件每行就是 `to_json()` 的一个结果；不包含任何网络协议。

## 文件

- `main.py`：命令行入口、循环、退出和可选记录。
- `camera.py`：摄像头打开、采集与释放。
- `hand_detector.py`：官方模型准备、CPU Tasks 初始化与推理。
- `data_processor.py`：结构化坐标与 JSON 序列化。
- `renderer.py`：镜像画面、骨骼、左右手状态和 FPS。
- `config.py`：摄像头、模型和识别阈值。
- `requirements.txt`：本机实测的直接依赖版本。
- `test_pipeline.py`：数据、异常清理和真实推理测试。
- `verify_camera.py`：有限帧真实弹窗检查，自动触发 S、Q 分支。
- `models/`：官方模型；`output/`：可选记录、截图及测试证据。
- `TEST_RESULTS.md`：本次实际结果及待人工验收项。

## 测试与排错

```powershell
.\.venv\Scripts\python.exe -m unittest -v test_pipeline
# 会打开真实摄像头，约 20 帧后自动退出；自动截图不代表真人双手验收
.\.venv\Scripts\python.exe verify_camera.py --json-output output\gui_probe.jsonl
```

图像测试在 `output/test_assets/` 中有官方 `thumb_up.jpg`、`right_hands.jpg` 时才执行；缺少会明确 skip，不会当作通过。
官方测试源：[MediaPipe hand_landmarker_test.py](https://github.com/google-ai-edge/mediapipe/blob/master/mediapipe/tasks/python/test/vision/hand_landmarker_test.py)。

无法打开摄像头：检查 Windows 设置 → 隐私和安全性 → 摄像头，允许桌面应用访问；关闭会议软件，尝试 `--camera 1` 或 `--backend msmf`。
无法显示窗口：确认使用 `.venv\Scripts\python.exe`，不要使用装有 headless OpenCV 的全局 Python。
设备不支持某分辨率时会使用设备实际设置；程序不会为了满足数字而伪造帧尺寸。
MediaPipe 原生库可能输出英文日志；以是否产生结果和退出码判断是否失败。
