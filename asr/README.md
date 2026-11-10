# 本地语音识别（可选）

「第二只耳朵」：按住语音键时，程序同时旁听麦克风，用本地 SenseVoice 识别。输入法没出字时自动用这里的结果。不联网、不开端口，每句约 100ms。

不装也能正常用，只是少了这层兜底。

## 安装

需要 [Node.js](https://nodejs.org/)（18 以上）。在程序运行目录（默认 `%USERPROFILE%\SayAllVoice\asr`）里：

```powershell
cd $env:USERPROFILE\SayAllVoice\asr
npm install sherpa-onnx-node@2
```

再从 sherpa-onnx 的 [asr-models 发布页](https://github.com/k2-fsa/sherpa-onnx/releases/tag/asr-models) 下载
`sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17.tar.bz2`，解压后把 `model.int8.onnx` 和 `tokens.txt` 放进 `asr\sense-voice\`：

```
SayAllVoice\
├─ SayAllVoice.exe
└─ asr\
   ├─ asr.cjs
   ├─ node_modules\        ← npm install 生成
   └─ sense-voice\
      ├─ model.int8.onnx   ← 约 228MB
      └─ tokens.txt
```

重启 SayAllVoice，日志里出现 `local asr ready` 就好了。

## 声音从哪来

用遥控器时，SayAll 会把遥控器的声音送进虚拟声卡（VB-Cable），程序从 `CABLE Output` 录音，只在按住语音键期间录。

## 协议

`asr.cjs` 走标准输入输出：输入一行 16kHz 单声道 16 位 PCM 文件路径，输出一行 `{"text":"…","ms":123}`。想换别的识别引擎，照这个格式写一个脚本替换即可。
