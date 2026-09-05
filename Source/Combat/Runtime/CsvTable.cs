using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 카탈로그 CSV 공용 파서. 따옴표 이스케이프("")와 인용부 안의 쉼표/개행을 지원하고,
    /// 각 행은 레코드가 시작한 실제 파일 행 번호를 보존한다(인용부 안 개행도 집계).
    /// 헤더는 전부 Trim + BOM 제거 후 이름으로 조회한다. 구조 오류(빈 파일, 빈 헤더,
    /// 열 수 불일치, 미종결 인용부)는 ArgumentException을 던지며, 도메인 값 검증은
    /// 각 컨버터의 소관이다. monster/card-vfx/status-effect/relic/player-profile
    /// 컨버터에 사본으로 존재하던 구현을 단일화했다.
    /// </summary>
    public sealed class CsvTable
    {
        private CsvTable(string name, IReadOnlyList<string> headers, IReadOnlyList<CsvRow> rows)
        {
            Name = name;
            Headers = headers;
            Rows = rows;
        }

        public string Name { get; }
        public IReadOnlyList<string> Headers { get; }
        public IReadOnlyList<CsvRow> Rows { get; }

        public static CsvTable Parse(string text, string name)
        {
            var records = ParseRecords(text ?? string.Empty).ToList();
            if (records.Count == 0)
            {
                throw new ArgumentException($"{name} is empty.");
            }

            var headers = records[0].Fields.Select(header => header.Trim().TrimStart('\uFEFF')).ToList();
            if (headers.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException($"{name}:1 has an empty header.");
            }

            var rows = new List<CsvRow>();
            for (var i = 1; i < records.Count; i++)
            {
                var record = records[i];
                if (record.Fields.Count == 1 && string.IsNullOrWhiteSpace(record.Fields[0]))
                {
                    continue;
                }

                if (record.Fields.Count != headers.Count)
                {
                    throw new ArgumentException($"{name}:{record.LineNumber} has {record.Fields.Count} columns but expected {headers.Count}.");
                }

                rows.Add(new CsvRow(name, record.LineNumber, headers, record.Fields));
            }

            return new CsvTable(name, headers, rows);
        }

        private static IEnumerable<CsvRecord> ParseRecords(string text)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;
            var lineNumber = 1;
            var recordLineNumber = 1;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        if (c == '\n') lineNumber++;
                        current.Append(c);
                    }

                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Length = 0;
                }
                else if (c == '\r' || c == '\n')
                {
                    fields.Add(current.ToString());
                    current.Length = 0;
                    yield return new CsvRecord(recordLineNumber, fields);
                    fields = new List<string>();
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    lineNumber++;
                    recordLineNumber = lineNumber;
                }
                else
                {
                    current.Append(c);
                }
            }

            if (inQuotes)
            {
                throw new ArgumentException("CSV contains an unterminated quoted field.");
            }

            if (current.Length > 0 || fields.Count > 0)
            {
                fields.Add(current.ToString());
                yield return new CsvRecord(recordLineNumber, fields);
            }
        }

        private sealed class CsvRecord
        {
            public CsvRecord(int lineNumber, IReadOnlyList<string> fields)
            {
                LineNumber = lineNumber;
                Fields = fields.ToList();
            }

            public int LineNumber { get; }
            public IReadOnlyList<string> Fields { get; }
        }
    }

    /// <summary>
    /// <see cref="CsvTable"/>의 데이터 한 행. 값은 원문 그대로 보관하며(Trim은 소비자 몫),
    /// LineNumber는 소스 파일의 실제 행 번호다. 헤더 이름 조회(TryGet)와 순서 접근(Fields)을
    /// 모두 지원한다 — 헤더 순서를 별도로 검증하는 소비자(카드 카탈로그)는 Fields를 쓴다.
    /// </summary>
    public sealed class CsvRow
    {
        private readonly Dictionary<string, string> values;

        public CsvRow(string fileName, int lineNumber, IReadOnlyList<string> headers, IReadOnlyList<string> fields)
        {
            FileName = fileName;
            LineNumber = lineNumber;
            Fields = fields;
            values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < headers.Count; i++)
            {
                values[headers[i]] = fields[i];
            }
        }

        public string FileName { get; }
        public int LineNumber { get; }
        public IReadOnlyList<string> Fields { get; }

        public bool TryGet(string column, out string value) => values.TryGetValue(column, out value);
    }
}
