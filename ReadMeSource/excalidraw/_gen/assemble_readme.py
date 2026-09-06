"""Assemble README.md from readme_template.md + code excerpts cut from the repo.

    python3 assemble_readme.py            # writes ../../../README.md
    python3 assemble_readme.py --check    # verifies README against repo (V3 checks)

Excerpts are cut by line range but each range is anchored to a signature line that must
appear at its first line, so a shifted file fails loudly instead of silently mis-cutting.
Leading common indentation is removed in the README; the check re-indents before comparing.
"""
import os
import re
import sys
import textwrap

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
TEMPLATE = os.path.join(HERE, "readme_template.md")
README = os.path.join(REPO, "README.md")

# key -> (path, first_line, last_line, signature that must be on first_line)
EXCERPTS = {
    "ex1": ("Source/Combat/Runtime/SeoulPlayup.Combat.Runtime.asmdef", 1, 17, "{"),
    "ex2": ("Source/Combat/Runtime/CombatState.cs", 3006, 3024, "private void BeginNextOverallTurn("),
    "ex3": ("Source/Combat/Runtime/MonsterAiPlanner.cs", 115, 137, "var reservedDestinations = new HashSet<HexCoord>();"),
    "ex4a": ("Source/Combat/Runtime/Cards/Attack/A01_Sweep.cs", 1, 13, "using SeoulPlayup.CardCore;"),
    "ex4b": ("Source/Combat/Runtime/CombatState.cs", 6199, 6218, "private void ConsumePlayedCard("),
    "ex5": ("Source/Map/Runtime/HexVisibilityRuntime.cs", 375, 406, "public HexVisibilitySafeCellInfo GetSafeCellInfo("),
    "ex6": ("Source/Map/Runtime/RunSeedStreams.cs", 21, 48, "/// <summary>몬스터 슬롯 셔플"),
    "ex7": ("Source/Map/Runtime/PlacementRandomizer.cs", 482, 506, "private static StageRandomizationPoolEntry WeightedPickWithRepeatDecay("),
}

BANNED = ["행동 트리를 도입", "셰이더 11종을 직접", "우회 불가", "성능 때문에 차폐 안 함", "EndAction이 유일한 전환 트리거",
          "타임라인 방식이라 순서 버그가 없다", "God Object를 해결", "12종 감사 툴", "CSV 34개", "원본 27개",
          "내가 설계", "제가 설계", "DEC-20", "트레이드오프"]
PERF = re.compile(r"[0-9] ?(ms|ns|µs)\b|[0-9]초|[0-9]×|[0-9]x\b")


def read_lines(rel):
    with open(os.path.join(REPO, rel), encoding="utf-8") as f:
        return f.read().split("\n")


def cut(key):
    rel, a, b, sig = EXCERPTS[key]
    lines = read_lines(rel)
    block = lines[a - 1:b]
    if sig not in block[0]:
        raise SystemExit(f"{key}: line {a} of {rel} does not contain signature {sig!r}: {block[0]!r}")
    return textwrap.dedent("\n".join(block)).rstrip("\n")


def assemble():
    tpl = open(TEMPLATE, encoding="utf-8").read()
    out = re.sub(r"\{\{EX:(\w+)\}\}", lambda m: cut(m.group(1)), tpl)
    with open(README, "w", encoding="utf-8") as f:
        f.write(out)
    print("README written:", len(out.split("\n")), "lines,", len(out.encode()), "bytes")


def check():
    ok = True
    md = open(README, encoding="utf-8").read()
    # 1. every fenced block is a (re-indentable) substring of a repo file
    blocks = re.findall(r"```\w*\n(.*?)```", md, flags=re.S)
    print(f"code blocks: {len(blocks)}")
    for i, blk in enumerate(blocks, 1):
        blk = blk.rstrip("\n")
        found = None
        for key, (rel, a, b, sig) in EXCERPTS.items():
            src = "\n".join(read_lines(rel))
            for indent in ("", "    ", "        ", "            "):
                cand = "\n".join((indent + ln) if ln.strip() else ln for ln in blk.split("\n"))
                if cand in src:
                    found = (key, rel, indent)
                    break
            if found:
                break
        print(f"  block {i}: {'OK ' + found[1] if found else 'MISSING'}")
        ok &= bool(found)
    # 2. banned phrases / perf numbers
    prose = re.sub(r"```.*?```", "", md, flags=re.S)
    for w in BANNED:
        if w in prose:
            print("  BANNED:", w); ok = False
    for m in PERF.finditer(prose):
        print("  PERF-NUMBER:", prose[max(0, m.start() - 20):m.end() + 10].replace("\n", " ")); ok = False
    # 3. image links exist
    for src in re.findall(r'src="([^"]+)"', md):
        if not os.path.exists(os.path.join(REPO, src)):
            print("  MISSING IMAGE:", src); ok = False
    # 4. backtick paths exist
    for p in set(re.findall(r"`((?:Source|Tests)/[^`]+)`", prose)):
        if not os.path.exists(os.path.join(REPO, p)):
            print("  MISSING PATH:", p); ok = False
    # 5. section lengths
    secs = re.split(r"\n## \d\. ", md)[1:]
    for s in secs:
        title = s.split("\n", 1)[0]
        print(f"  section '{title}': {len(s.split(chr(10)))} lines")
    print("ALL OK" if ok else "CHECK FAILED")
    return ok


if __name__ == "__main__":
    if "--check" in sys.argv:
        sys.exit(0 if check() else 1)
    assemble()
