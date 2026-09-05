#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 저작 CSV의 <b>데이터 행 수</b>를 센다.
    ///
    /// 왜 있나(2026-08-31 T4): 카탈로그 크기를 <c>Has.Count.EqualTo(59)</c>처럼 오늘의 숫자로 박아
    /// 두면, 기획이 정상적으로 콘텐츠를 늘릴 때마다 <b>무관한 테스트가 거짓 경보로 깨진다.</b>
    /// 그런 일이 반복되면 사람이 테스트를 안 믿게 되고, 그게 진짜 손실이다.
    ///
    /// 🔑 <b>이건 동어 반복이 아니다.</b> 「행 수 = 엔트리 수」는 <b>변환이 행을 조용히 흘리지
    /// 않았다</b>는 계약이다 — 파서가 한 줄을 건너뛰어도 지금까지는 상수를 같이 고쳐 버리면
    /// 아무도 몰랐다. 이제는 CSV를 고치지 않는 한 그 차이가 그대로 드러난다.
    ///
    /// ⚠️ <b>개수가 곧 계약인 곳에는 쓰지 않는다.</b> 「보스 페이즈는 정확히 3단계」처럼 숫자 자체가
    /// 규칙이거나, 계획 문서와의 동기화를 강제하려고 일부러 박아 둔 자리는 상수로 남기고
    /// <b>왜 고정인지 주석을 단다</b>(예: 유물 28종 ↔ 도감 계획 §1.1).
    /// </summary>
    internal static class AuthoredCsvRows
    {
        /// <summary>헤더 한 줄을 뺀 비어 있지 않은 행 수.</summary>
        internal static int Count(string csvPath)
        {
            return File.ReadAllLines(csvPath, new UTF8Encoding(false, true))
                .Skip(1)
                .Count(line => !string.IsNullOrWhiteSpace(line));
        }
    }
}
#endif
