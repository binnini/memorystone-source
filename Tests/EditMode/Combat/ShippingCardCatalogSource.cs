#if UNITY_EDITOR
using System.IO;
using System.Text;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 출하 <c>cards.csv</c>를 <b>런타임과 같은 파싱 경로</b>로 읽어 카드 카탈로그를 만든다.
    ///
    /// 왜 공용인가(2026-08-31 T3): <c>CombatEffectSourceClassifier</c>가 ID switch 대신 카탈로그의
    /// <c>ActionType</c>을 읽게 되면서, 분류를 확인하는 테스트마다 출하 카탈로그가 필요해졌다.
    /// 파일마다 로더를 한 벌씩 두면 <b>그 로더들이 서로 어긋날 수 있고</b>, 이 트랙이 지우려는 것이
    /// 바로 그 사본이다. 로더는 여기 하나뿐이다.
    ///
    /// ⚠️ 손으로 옮겨 적은 픽스처를 쓰지 않는다 — 저작과 어긋난 순간 픽스처는 거짓말을 시작한다
    /// (2026-08-02 U04 호롱불 사고, <see cref="ShippingCardCsvEffectTests"/> 주석).
    /// </summary>
    internal static class ShippingCardCatalogSource
    {
        private static CardCatalogDefinition cached;

        /// <summary>
        /// 출하 카탈로그. CSV 파싱이 싸지 않으므로 프로세스 안에서 한 번만 읽는다 —
        /// 카탈로그는 불변이라 공유해도 테스트끼리 간섭하지 않는다.
        /// </summary>
        internal static CardCatalogDefinition Load()
        {
            if (cached != null)
            {
                return cached;
            }

            var asset = ScriptableObject.CreateInstance<CardCatalogAsset>();
            try
            {
                asset.SetRows(CardCatalogAsset.ParseCsvText(
                    File.ReadAllText(CombatCsvPaths.CardsCsv, new UTF8Encoding(false, true))));
                cached = asset.ToCardCatalogDefinition(CombatConfig.Default);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }

            Assert.That(cached.Entries, Is.Not.Empty, "출하 카드를 하나도 못 읽었다 — 감사가 빈 채로 통과하고 있다.");
            return cached;
        }
    }
}
#endif
