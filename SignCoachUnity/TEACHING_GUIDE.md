# 教学素材与交互升级

先读词汇和素材状态，再观看来源、阅读说明、确认提示，最后开始练习。
首页及词汇浏览不启动摄像头。摄像头只有在点击练习/启动服务后才打开。

## 本轮内容边界

8个词汇各有2条候选来源记录（共16条）。A是可核实的官方/专业规范来源候选，
B是仍需核验具体教学片段的来源，C仅参考。课程索引不等于已完整看过视频。
所有真实素材仍为 pending_review，0份由程序认证。动作文字说明尚未专业审核时显示待补充。
“再见、帮助、喜欢”有公开CC BY图像形成的审核预览及实际MediaPipe提取JSONL。
它们不是已审核标准视频：源图像采样率、镜像约定未确认，不能用于正式标准比较。
详细出处、许可、未知字段见 Assets/StreamingAssets/Teaching/catalog.json 和 CREDITS.md。

## 运行

用Unity2022.3.62f3c1打开本项目，或运行本轮Windows Release包的启动程序.cmd。
词汇库 → 选择词汇 → 默认先学后练；在教学页可播放、暂停、0.5倍慢放、重播、重复播放及拖动进度。
未获下载许可的来源通过“打开原始教学来源”在用户浏览器观看，不在后台擅自下载。
本地预览始终有待审核与预览时基提示。

勾选已阅读提示，选择“我已了解，开始练习”或“示范伴随练习”。
伴随练习左边显示来源预览，右边显示真实摄像头与骨骼，可以录制、停止、取消、保存。
新练习沿用原录制器，以新take保存，保持JSONL和原坐标，不覆盖旧文件。
有未保存草稿时先保存或取消才能退出。可返回教学页重看。

Python仍可独立运行：
```
.venv\Scripts\python.exe main.py --tcp
```
如需Unity内摄像头背景，使用该Player状态文件中的preview路径：
```
.venv\Scripts\python.exe main.py --tcp --preview-file "完整预览路径.bin"
```
Release内“启动视觉服务”自动传入专属进程的预览文件路径。
新增的是可选本机8字节毫秒时间戳+JPEG文件，TCP JSONL完全未变；无第二个摄像头实例。
Python发送未镜像图像，Unity只显示时镜像一次。超时500ms清除旧画面，骨骼与预览超过250ms不同步时不叠加。

## 审核与标准流程

素材审核入口可查看每个词汇的来源、平台、机构、体系、许可、预览、提取状态与当前审核状态。
填写审核人、专业身份和具体备注，再勾选专业人工确认，进行二次确认后记录通过/待补充/不适用。
本地工具不验证审核人的真实职业身份，请由项目负责人安排合格专业人员操作。
审核记录独立保存到Application.persistentDataPath/teaching_reviews；可用-reviewRoot指定可写目录。
每次审核保留audit.jsonl，原始来源文件不会被修改。素材元数据/视频/JSONL变化会使旧通过记录失效。
开发者和自动化测试不会审核真实材料。

标准比较同时要求：专业审核通过、明确处理许可、时基核实、mirror_status=unmirrored、
hand_comparison_approved=true、存在完整JSONL，并通过原有数据/可靠帧质量校验。
后续补充原始视频时，需由专业人员确认上述元数据及动作说明依据，再重新审核。
只有教学来源审核通过但没有可用序列时，页面会继续提示“标准序列待补充”。
比较资格与教学来源审核状态不是同一个开关。

正式比较复用SequenceComparer.LoadJsonl、原特征器及DTW。不同像素纵横比只通过
TeachingSequenceAdapter对算法副本做度量适配，原文件、时间、标签不变。
当前练习摄像头沿用640×480配置；若未来调整配置，要同时核对适配器的相机纵横比。
未审核数据仍可在原序列比较页面用于算法演示，不能称为手语正确率。
反馈的分组距离来自现有DTW结果，量纲不同，不能把最高的一组自动称为语言错误。

## 数据与接口

```
Assets/StreamingAssets/Teaching/
  catalog.json                         # 16条来源记录
  CREDITS.md
  data/reference/
    verified/                          # 当前无已审核标准
    pending_review/{goodbye,help,like}/
      metadata.json
      candidate_preview.mp4
      candidate_001.jsonl
      candidate_001.extraction.json
    unverified/                        # 保留分类入口，不移动旧文件
data/reference/hello/take_001.jsonl      # 原始旧录制保持原位
data/reference/hello/take_002.jsonl
```

TeachingCatalog读取目录及审核记录；CanCompare给出阻断原因。
TeachingPanel只调用既有录制/比较API；CompareReference加载标准JSONL与本词用户练习。
Python extract_teaching_video.py对明确获准的本地视频工作，必须传--permission-record，
该JSON记录需含source_url、license和processing_allowed=true。已有目标文件一律拒绝覆盖。
不会猜测视频镜像或自行交换左右标签。

## 重建与测试

Python: `.venv\Scripts\python.exe -m unittest discover -v`。
Unity: `Unity.exe -batchmode -nographics -quit -projectPath "项目路径" -executeMethod BuildAndVerify.Execute -logFile "日志路径"`。
构建前执行原录制、比较、通信检查及新增TeachingTests；仍使用BuildOptions.None。
独立视觉服务仍通过SignCoachLite/build_vision.ps1打包，需要requirements-build.txt。
验收脚本在SignCoachLite/output/teaching。测试结果与未完成验收详见同目录测试记录。
