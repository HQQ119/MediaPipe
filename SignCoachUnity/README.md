# SignCoach Lite · 最终学习交互版

当前学习词汇为：再见、谢谢、对不起、吃饭、帮助、喜欢。六个词均有本地观察视频和关键点数据。
“你好”“喝水”已从首页、词汇库、录制和比较选项中移除；已有历史录制文件保留供回归验证。
默认词汇为“再见”。最新 Windows 交付包可直接双击 SignCoachLite.exe，视觉服务由 Unity 在后台启动。
片段由公开采样图像组成，播放速度不代表原始动作速度；比较结果不表示手语语义正确率。

结果页与比较页顶部显示0～100分的动作相似度分数和进度条，保留一位小数。分数越高，已对齐手部动作越接近。
低质量数据标注“有效片段参考分”并保留质量警告；查看计算方式可见原DTW转换公式。切换比较文件后需重新点击开始比较。


当前评分为v2.1，默认“入门练习”，可切换“精细对照”；同一DTW路径与原始距离使用不同、可复算的分数换算。质量保护保留。

当前动作比较为v2：明确执行手配对、每手独立轨迹基准、局部手型与朝向分离、受约束DTW、统一损失量纲及分项得分。入镜前后等待与中途缺失分别统计，参考不足和练习不足分别提示。详见COMPARISON_GUIDE.md顶部及COMPARISON_TEST_RESULTS.md的v2结果。

下面各阶段记录保留为历史说明，最新测试以本次交付包 Tests 目录为准。

第五阶段通过 LearningApp 复用原模块，提供首页、词汇库、示范素材状态、练习和结果页面。
正常 Windows Release 交付包含独立视觉服务；双击“启动程序.cmd”，按界面提示启动视觉服务。
原数据未升级为标准，暂无可靠示范视频；本版本可演示采集、用户练习保存与未审核数据的诊断比较。
构建仍调用 BuildAndVerify.Execute，BuildOptions.None 为非开发构建。具体启动、限制、测试与演示说明在最终包根 README.md。

第四阶段已有动作序列特征、质量处理、DTW、相似度与时间轴比较界面。详见 [COMPARISON_GUIDE.md](COMPARISON_GUIDE.md) 和 [COMPARISON_TEST_RESULTS.md](COMPARISON_TEST_RESULTS.md)。
已有两份录制均未审核，结果不是手语正确率；离线比较已有数据无需启动 Python。

第三阶段增加了词汇选择、完整序列录制、保存校验和历史管理，操作与数据接口见 [RECORDING_GUIDE.md](RECORDING_GUIDE.md)。
真人录制样本为未审核测试数据，不能当作标准手语示范。实际结果见 [RECORDING_TEST_RESULTS.md](RECORDING_TEST_RESULTS.md)。

独立 Unity 项目，使用本机 Unity **2022.3.62f3c1**。没有修改其他 IK 项目。
TCP 接收线程、JSONL 分帧、21 点校验、双手骨骼对象复用、简单调试界面。
仅使用 Unity 内置模块，无第三方网络/JSON 包。

## 运行

先在 Python 项目执行（也可先打开 Unity，客户端会持续重试）：

```powershell
cd 'D:\测试项目\unity\SignCoachLite'
.\.venv\Scripts\python.exe main.py --tcp --json-output output\stage2\camera_session.jsonl
```

双击 `Build/SignCoachLite.exe` 运行已构建的 Windows 调试程序。
也可用对应 Unity Editor 打开本目录，打开 `Assets/Scenes/SignCoachDebug.unity` 后按 Play。
默认连接 `127.0.0.1:5005`。Python 可用 `--port` 改端口，但 Unity 默认地址和端口需同步改 `HandTcpClient` 构造配置。
不要为了切换设备在 Unity 中新增第二个摄像头；Python 用 `--camera 1` 切换。

界面显示连接、手数、左右状态、接收/应用帧率和渲染 FPS。接收 FPS 是主线程应用最新帧的平滑频率，并不是模型单独推理速度。
手部对象最多两套，每套 21 点、21 骨骼连接。每帧只更新位置/颜色/可见性，不反复销毁对象。
Unity 骨骼采用镜像显示，与 Python OpenCV 画面同方向；Y 轴翻转；不将模型标签按位置改写。
关闭窗口或按 Esc 退出；Editor 停止 Play 会关闭 Socket 与接收线程。

## 构建与自动检查

```powershell
& 'D:\unity\Editor\Unity.exe' -batchmode -nographics -quit `
  -projectPath 'D:\测试项目\unity\SignCoachUnity' `
  -executeMethod BuildAndVerify.Execute `
  -logFile 'D:\测试项目\unity\SignCoachLite\output\stage2\unity_build.log'
```

Editor 脚本先运行 JSON/分帧/坐标/真实本机 TCP 连接重连/线程关闭检查，再保存调试场景并构建 Windows64 Mono。
检查摘要在 `Verification/editor_tests.txt`；构建失败会抛出异常。
本机用此版本编译，不声称其他 Unity 版本均已测试。

## 联合验收

1. 不启动 Python 时打开 Unity，应保持响应并提示重试。
2. 启动 Python `--tcp`；Unity 应连接并持续显示同一摄像头会话的数据。
3. 单独举左手、右手，各保持 5 秒，对照两窗口位置、骨骼、颜色和标签。
4. 双手同时出现并缓慢移动，保持两手分开；核对手数 2 和完整骨骼。
5. 双手移出画面，Unity 应清除骨骼；关闭 Python，Unity 应显示断开并继续响应。
6. 重新启动 Python，Unity 应自动连接；关闭 Unity 后 Python 应继续识别并等待客户端。

左右分类沿用第一阶段，少量跳动不作强制纠正。按动作阶段记录真实结果，不将协议模拟测试当作真人验收。
完整运行说明和协议见旁边 Python 项目的 `STAGE2_PROTOCOL.md`、`STAGE2_TEST_RESULTS.md`。

## 测试证据开关

开发构建允许显式传入 `-signcoachEvidence "绝对目录"`，在该目录保存主线程已应用帧的 JSONL、可见手数状态及启动截图。
该开关只用于验收，不改变网络数据；不传时不写这些文件。在目录中创建 `quit.request` 会让验证程序正常退出。
重新测试使用新目录，避免上一次退出请求文件使程序立即退出。真实截图与原始日志保存在本地，不自动上传。
