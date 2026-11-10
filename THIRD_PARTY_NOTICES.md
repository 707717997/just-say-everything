# 第三方组件与数据

| 组件 | 用在哪 | 许可证 | 是否随本仓库分发 |
| --- | --- | --- | --- |
| [pypinyin](https://github.com/mozillazg/python-pinyin) | 生成 `src/pinyin.txt`（汉字 → 拼音对照表，用于近音纠错），脚本见 `tools/gen_pinyin.py` | MIT | 只分发生成出来的对照表 |
| [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx)（npm 包 `sherpa-onnx-node`） | 可选的本地语音识别（`asr/asr.cjs`） | Apache-2.0 | 否，用户自行 `npm install` |
| [SenseVoice](https://github.com/FunAudioLLM/SenseVoice) 模型 | 可选的本地语音识别模型 | 代码为 MIT；模型权重以其发布页标注的许可证为准 | 否，用户自行下载 |
| .NET Framework 4.x、System.Speech、UI Automation | 运行时 | Windows 系统组件 | 否 |

## pypinyin 的许可证原文

```
The MIT License (MIT)

Copyright (c) 2016 mozillazg, 闲耘 <hotoo.cn@gmail.com>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## 商标与名称

小米、无线麦 SayAll、微信、抖音、Claude 等名称及商标归各自权利人所有，本项目只用它们说明兼容性，与这些公司没有关联。
