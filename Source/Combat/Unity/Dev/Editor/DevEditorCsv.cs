using System.Collections.Generic;
using System.Text;

namespace SeoulPlayup.Combat.Unity.Dev.Editor
{
    /// <summary>
    /// Dev 에디터 임포터 공용의 관용적 CSV 한 줄 분리기. 큰따옴표 필드(이스케이프 "" 포함)를
    /// 지원하되 열 수나 인용 종결을 강제하지 않는다 — 워크시트성 CSV를 다루는 임포터용이라
    /// 엄격 파서(SeoulPlayup.Combat.Runtime.CsvTable)와 달리 관대한 동작이 의도다.
    /// CombatAttackTimingTableCsvImporter/SoundCatalogCsvImporter에 사본으로 있던 구현을 단일화했다.
    /// </summary>
    internal static class DevEditorCsv
    {
        // Minimal CSV splitter: handles optional double-quoted fields with embedded commas.
        public static string[] SplitLine(string line)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }

            result.Add(sb.ToString());
            return result.ToArray();
        }
    }
}
