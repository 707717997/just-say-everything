// 本地语音识别小进程。SayAllVoice 启动它，走标准输入输出，不开端口。
// 输入一行：16kHz 单声道 16 位 PCM 文件的路径。输出一行 JSON：{"text":"…","ms":123}
const fs = require('fs');
const path = require('path');
const readline = require('readline');
const sherpa = require('sherpa-onnx-node');

const dir = path.join(__dirname, 'sense-voice');
const recognizer = new sherpa.OfflineRecognizer({
  featConfig: { sampleRate: 16000, featureDim: 80 },
  modelConfig: {
    senseVoice: { model: path.join(dir, 'model.int8.onnx'), language: 'zh', useInverseTextNormalization: 1 },
    tokens: path.join(dir, 'tokens.txt'),
    numThreads: 2,
    provider: 'cpu',
    debug: 0,
  },
});

function send(o) { process.stdout.write(JSON.stringify(o) + '\n'); }

function recognize(file) {
  const t0 = Date.now();
  const buf = fs.readFileSync(file);
  const n = buf.length >> 1;
  const samples = new Float32Array(n);
  for (let i = 0; i < n; i++) samples[i] = buf.readInt16LE(i * 2) / 32768;
  const s = recognizer.createStream();
  s.acceptWaveform({ sampleRate: 16000, samples });
  recognizer.decode(s);
  const r = recognizer.getResult(s);
  return { text: (r.text || '').trim(), ms: Date.now() - t0 };
}

send({ ready: true });
readline.createInterface({ input: process.stdin }).on('line', (line) => {
  const f = line.trim();
  if (!f) return;
  try { send(recognize(f)); } catch (e) { send({ text: '', error: String(e.message || e) }); }
});
process.stdin.on('end', () => process.exit(0));
