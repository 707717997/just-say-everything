"""生成 src/pinyin.txt：常用汉字区每个字一行「汉字+不带声调的拼音」，供近音纠错用。

    pip install pypinyin
    python tools/gen_pinyin.py
"""
import io
import os
from pypinyin import Style, lazy_pinyin

out = []
for cp in range(0x4E00, 0x9FA6):  # 一 … 龥
    c = chr(cp)
    p = lazy_pinyin(c, style=Style.NORMAL, errors="ignore")
    if p and p[0].isascii():
        out.append(c + p[0])

dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "pinyin.txt")
io.open(dst, "w", encoding="utf-8", newline="\n").write("\n".join(out))
print(len(out), "lines ->", os.path.normpath(dst))
