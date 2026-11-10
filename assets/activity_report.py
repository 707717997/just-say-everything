"""汇总常用功能。读 SayAllVoice 的使用记录、语音日志、SayAll 按键记录和 Claude 会话元数据。
用法：python activity_report.py [天数，默认 7]
只读本机文件，不联网。不读聊天内容和网页内容。"""
import collections
import datetime as dt
import glob
import io
import json
import os
import re
import sys

sys.stdout.reconfigure(encoding='utf-8')
days = int(sys.argv[1]) if len(sys.argv) > 1 else 7
since = dt.datetime.now() - dt.timedelta(days=days)
A = os.path.expandvars(r'%APPDATA%')


def h(sec):
    return '%.1f 小时' % (sec / 3600) if sec >= 3600 else '%d 分钟' % (sec / 60)


proc = collections.Counter()
title = collections.Counter()
idle = 0
combos = collections.Counter()
hops = collections.Counter()
files = sorted(glob.glob(os.path.join(A, 'SayAllVoice', 'activity', '*.jsonl')))
for f in files:
    for l in io.open(f, encoding='utf-8-sig', errors='replace'):
        try:
            d = json.loads(l)
        except ValueError:
            continue
        if dt.datetime.strptime(d['m'], '%Y-%m-%d %H:%M') < since:
            continue
        if 's' in d:
            if d.get('idle'):
                idle += d['s']
            proc[d['p'] or '(未知)'] += d['s']
            title[(d['p'], re.sub(r'\s*[-—]\s*(Google Chrome|DeepSeek Harness)$', '', d['t']))] += d['s']
        elif 'k' in d:
            combos[(d['k'], d['p'])] += d['n']
        elif 'hop' in d:
            hops[d['hop']] += d['n']
print('== 前台程序（%d 天，%d 个文件，其中 %s 无键鼠操作）' % (days, len(files), h(idle)))
for p, s in proc.most_common(15):
    print('  %-10s %s' % (h(s), p))
print('== 窗口')
for (p, t), s in title.most_common(25):
    print('  %-10s %s | %s' % (h(s), p, t[:50]))
print('== 快捷键')
total = collections.Counter()
for (k, p), n in combos.items():
    total[k] += n
for k, n in total.most_common(20):
    where = [p for (kk, p), _ in combos.most_common() if kk == k][:3]
    print('  %5d  %-18s %s' % (n, k, ' '.join(where)))
print('== 程序间切换')
for k, n in hops.most_common(12):
    print('  %5d  %s' % (n, k))

print('== 遥控器按键实际动作')
log = os.path.expandvars(r'%LOCALAPPDATA%\SayAll\Logs\sayall-diagnostic.log')
fire = collections.Counter()
if os.path.exists(log):
    for l in io.open(log, encoding='utf-8', errors='replace'):
        if ' map_fire ' in l:
            m = re.search(r'button=(\S+) trigger=(\S+) action=(\S+)(?: chord=(\S+))?', l)
            if m:
                fire[(m.group(1), m.group(2), m.group(4) or m.group(3))] += 1
for (b, t, a), n in fire.most_common(20):
    print('  %5d  %s %s -> %s' % (n, b, t, a))

print('== 语音指令')
said = collections.Counter()
fail = collections.Counter()
vl = os.path.join(A, 'SayAllVoice', 'sayallvoice.log')
for l in io.open(vl, encoding='utf-8', errors='replace'):
    try:
        t = dt.datetime.strptime(l[:19], '%Y-%m-%d %H:%M:%S')
    except ValueError:
        continue
    if t < since:
        continue
    m = l[25:].strip()
    if m.startswith('execute '):
        said[m.split(' ', 2)[2]] += 1
    elif m.startswith(('voice: no text', 'race LOST', 'ask:')):
        fail[m.split(',')[0][:24]] += 1
for k, n in said.most_common(20):
    print('  %5d  %s' % (n, k))
print('  问题：', dict(fail))

print('== Claude 会话（近 %d 天有活动）' % days)
ses = []
for f in glob.glob(os.path.join(A, 'Claude', 'claude-code-sessions', '*', '*', 'local_*.json')):
    try:
        d = json.load(io.open(f, encoding='utf-8'))
    except ValueError:
        continue
    last = dt.datetime.fromtimestamp((d.get('lastActivityAt') or 0) / 1000)
    if last >= since and 'spawnedFrom' not in d:
        ses.append((d.get('completedTurns') or 0, d.get('title', ''), bool(d.get('isStarred')), d.get('cwd', '')))
print('  共 %d 个' % len(ses))
for n, t, s, c in sorted(ses, key=lambda x: -x[0])[:15]:
    print('  %5d 轮 %s %s | %s' % (n, '★' if s else ' ', t[:30], c))
