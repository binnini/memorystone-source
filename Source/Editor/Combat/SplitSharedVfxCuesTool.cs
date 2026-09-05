using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.EditorTools.Combat
{
    /// <summary>
    /// 일회성(멱등) 마이그레이션 — 몬스터 VFX 큐를 <b>패턴 전용</b>으로 분리한다(2026-09-04 사용자 확정).
    ///
    /// <para>배경: 런타임 카탈로그는 <c>monster_pattern_vfx_bindings.csv</c>의 바인딩마다 엔트리 하나를 만들고
    /// sourceRef를 패턴별로 찍는다(VfxCueCatalogTools.RebuildDefaultCatalogFromCombatCsv). 하지만 프리팹·스케일·
    /// 오프셋·회전·수명 같은 <b>큐 값</b>은 <c>combat_vfx_cues.csv</c>의 큐 행 하나에 있고, 그 큐를 여러 패턴이
    /// 공유했다 — 그래서 lab에서 큐 하나를 편집하면 그 큐를 쓰는 모든 패턴이 연쇄로 바뀌었다.</para>
    ///
    /// <para>이 툴은 2개 이상 바인딩이 공유하는 큐마다, 큐의 sourceRef와 patternId가 일치하는 바인딩('keeper')만
    /// 원본 큐에 남기고 나머지 바인딩은 <b>값을 그대로 복사한</b> 새 큐(Vxxx)로 분리한 뒤 바인딩을 repoint한다.
    /// 값이 동일하므로 인게임 그림은 바뀌지 않고, 이후 각 패턴을 독립적으로 튜닝할 수 있다.</para>
    ///
    /// <para>텍스트 레벨 수술이다: 손대지 않는 행은 바이트 그대로 두고, repoint된 바인딩 행만 다시 emit하며,
    /// 새 큐 행은 파일 끝에 append한다(기존 lossy round-trip 함정 회피). 재실행하면 공유 큐가 없어 no-op다.</para>
    /// </summary>
    public static class SplitSharedVfxCuesTool
    {
        private const string CuesPath = "Assets/Data/Combat/Presentation/Source/combat_vfx_cues.csv";
        private const string BindingsPath = "Assets/Data/Combat/Presentation/Source/monster_pattern_vfx_bindings.csv";

        // combat_vfx_cues.csv 컬럼 인덱스
        private const int CueIdCol = 0;
        private const int CueSourceRefCol = 13;
        private const int CueNoteCol = 15;

        // monster_pattern_vfx_bindings.csv 컬럼 인덱스
        private const int BindPatternCol = 0;
        private const int BindCueCol = 1;
        private const int BindOrderCol = 3;

        [MenuItem("Tools/Seoul Playup/Combat/Split Shared VFX Cues (per-pattern)")]
        public static void Run()
        {
            var (cueNl, cueHadTrailing, cueLines) = ReadLines(CuesPath);
            var (bindNl, bindHadTrailing, bindLines) = ReadLines(BindingsPath);

            // 큐 행 파싱: cueId -> fields
            var cueFieldsById = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (var i = 1; i < cueLines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(cueLines[i])) continue;
                var f = ParseCsv(cueLines[i]);
                if (f.Count > CueIdCol) cueFieldsById[f[CueIdCol]] = f;
            }

            // 바인딩 행 파싱(파일 순서 보존)
            var bindings = new List<(int lineIdx, List<string> fields)>();
            for (var i = 1; i < bindLines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(bindLines[i])) continue;
                bindings.Add((i, ParseCsv(bindLines[i])));
            }

            // cueId -> 그 큐를 쓰는 바인딩들(bindings 리스트 인덱스)
            var byCue = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (var b = 0; b < bindings.Count; b++)
            {
                var cid = bindings[b].fields[BindCueCol];
                if (!byCue.TryGetValue(cid, out var list)) { list = new List<int>(); byCue[cid] = list; }
                list.Add(b);
            }

            var nextNum = cueFieldsById.Keys
                .Where(k => k.Length > 1 && k[0] == 'V' && int.TryParse(k.Substring(1), out _))
                .Select(k => int.Parse(k.Substring(1)))
                .DefaultIfEmpty(0)
                .Max() + 1;

            var newCueRows = new List<string>();
            var log = new List<string>();

            foreach (var cueId in byCue.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var members = byCue[cueId];
                if (members.Count <= 1) continue; // 이미 전용
                if (!cueFieldsById.TryGetValue(cueId, out var cueFields))
                {
                    Debug.LogWarning($"[SplitVfxCues] binding references missing cue '{cueId}'; skipped.");
                    continue;
                }

                var cueSourceRef = cueFields.Count > CueSourceRefCol ? cueFields[CueSourceRefCol] : string.Empty;
                var ordered = members.OrderBy(b => bindings[b].lineIdx).ToList();
                // keeper = 큐 sourceRef와 patternId가 일치하는 바인딩(있으면), 없으면 파일 첫 바인딩.
                var keeper = ordered.FirstOrDefault(
                    b => string.Equals(cueSourceRef, $"monster.pattern.{bindings[b].fields[BindPatternCol]}", StringComparison.Ordinal));
                var hasKeeperMatch = ordered.Any(
                    b => string.Equals(cueSourceRef, $"monster.pattern.{bindings[b].fields[BindPatternCol]}", StringComparison.Ordinal));
                if (!hasKeeperMatch) keeper = ordered[0];

                foreach (var b in ordered)
                {
                    if (b == keeper) continue;
                    var pattern = bindings[b].fields[BindPatternCol];
                    var order = bindings[b].fields.Count > BindOrderCol ? bindings[b].fields[BindOrderCol] : "1";
                    var newId = $"V{nextNum:000}";
                    nextNum++;

                    var nf = new List<string>(cueFields);
                    nf[CueIdCol] = newId;
                    if (nf.Count > CueSourceRefCol) nf[CueSourceRefCol] = $"monster.pattern.{pattern}";
                    if (nf.Count > CueNoteCol)
                    {
                        nf[CueNoteCol] = $"{pattern} 전용 큐(2026-09-04 {cueId}에서 분리 — 각 패턴 전용 큐 정책). "
                                         + $"값은 분리 시점 {cueId}와 동일이라 그림 무변경, 이후 독립 튜닝 가능.";
                    }

                    newCueRows.Add(EmitCsv(nf));

                    var repointed = new List<string>(bindings[b].fields);
                    repointed[BindCueCol] = newId;
                    bindLines[bindings[b].lineIdx] = EmitCsv(repointed);

                    log.Add($"{pattern} order{order}: {cueId} -> {newId}");
                }
            }

            if (newCueRows.Count == 0)
            {
                Debug.Log("[SplitVfxCues] nothing to split — every cue is already bound by a single pattern.");
                return;
            }

            File.Copy(CuesPath, CuesPath + ".bak", overwrite: true);
            File.Copy(BindingsPath, BindingsPath + ".bak", overwrite: true);

            var cueOut = new List<string>(cueLines);
            cueOut.AddRange(newCueRows);
            WriteLines(CuesPath, cueNl, cueHadTrailing, cueOut);
            WriteLines(BindingsPath, bindNl, bindHadTrailing, bindLines);

            AssetDatabase.Refresh();
            Debug.Log($"[SplitVfxCues] minted {newCueRows.Count} per-pattern cues (V{nextNum - newCueRows.Count:000}..V{nextNum - 1:000}):\n"
                      + string.Join("\n", log));
        }

        private static (string newline, bool hadTrailing, List<string> lines) ReadLines(string path)
        {
            var text = File.ReadAllText(path);
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var hadTrailing = text.EndsWith("\n");
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            return (newline, hadTrailing, lines);
        }

        private static void WriteLines(string path, string newline, bool hadTrailing, List<string> lines)
        {
            var body = string.Join(newline, lines);
            if (hadTrailing) body += newline;
            File.WriteAllText(path, body, new UTF8Encoding(false));
        }

        // 완전 인용 CSV 파서(RFC4180: 필드 내 ""는 이스케이프된 "). 이 CSV들은 모든 필드를 "..."로 감싼다.
        private static List<string> ParseCsv(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(c);
                }
            }

            fields.Add(sb.ToString());
            return fields;
        }

        private static string EmitCsv(IEnumerable<string> fields)
        {
            return string.Join(",", fields.Select(f => "\"" + (f ?? string.Empty).Replace("\"", "\"\"") + "\""));
        }
    }
}
