#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 연출 <b>누락</b>의 게이트(2026-09-05 실플레이 G4). 이번에 나온 두 결함 — 만반의 준비(D07)만
    /// 방어 카드 중 유일하게 전용 큐가 없어 범용 폴백으로 떨어졌고, 봉인만 상태이상 VFX가 통째로
    /// 없었다 — 은 <b>테스트가 아니라 감사 도구</b>가 찾았다. 감사는 사람이 돌려야 잡히고 테스트는
    /// 저절로 돈다. 같은 구멍이 다시 열리지 않게 여기에 잠근다.
    /// </summary>
    [Category("ShippingData")]
    public sealed class DefendCardAndStatusVfxCoverageTests
    {
        private static EffectVfxCatalog LoadShippingCatalog()
        {
            var catalog = Resources.Load<EffectVfxCatalog>("Combat/DefaultEffectVfxCatalog");
            Assert.That(catalog, Is.Not.Null, "출하 VFX 카탈로그를 못 읽었다 — 게이트가 빈 채로 통과하고 있다.");
            return catalog;
        }

        /// <summary>
        /// 방어 카드는 <b>전부</b> 자기 큐를 갖는다. 하나만 빠지면 그 카드만 범용 폴백(지연 0)으로
        /// 떨어져 다른 방어 카드와 <b>타이밍이 달라진다</b> — 같은 동작인데 손맛이 다른 이유가 된다.
        /// </summary>
        [Test]
        public void EveryDefendCardHasItsOwnVfxCue()
        {
            var cardCueIds = new HashSet<string>(
                CombatCardVfxCsvConverter.ConvertFile(CombatCsvPaths.CardVfxCuesCsv).Select(cue => cue.CardId),
                System.StringComparer.Ordinal);

            var missing = CardCatalogAsset
                .ParseCsvText(File.ReadAllText(CombatCsvPaths.CardsCsv, System.Text.Encoding.UTF8))
                .Where(row => row.Type == "방어")
                .Where(row => !cardCueIds.Contains(row.Id))
                .Select(row => $"{row.Id} {row.Name}")
                .ToArray();

            Assert.That(
                missing, Is.Empty,
                "방어 카드에 전용 큐가 없으면 범용 폴백으로 떨어져 다른 방어 카드와 그림·지연이 갈린다: "
                + string.Join(", ", missing));
        }

        /// <summary>
        /// 플레이어가 <b>실제로 걸릴 수 있는 해로운 상태이상</b>은 전부 VFX 커버가 있어야 한다.
        /// 아직 없는 것은 <see cref="PendingStatusVfxKinds"/>에 명시적으로 실려야 하고, 그림이
        /// 들어오는 순간 이 테스트가 먼저 빨개져서 목록을 지우게 만든다(배지 아트 게이트와 같은 규약).
        /// </summary>
        private static readonly StatusEffectKind[] PendingStatusVfxKinds =
        {
            // 2026-09-05: 봉인에 이어 무장 해제·쇠약·허점까지 채워 목록이 비었다. 지우지 않고 빈 채로
            // 남긴다 — 새 해로운 상태이상을 만들면 아트가 오기 전까지 여기 넣어야 「걸려도 화면에
            // 아무 일도 안 일어나는」 상태로 조용히 출하되지 않는다.
        };

        [Test]
        public void EveryDebuffStatusHasAVfxCueOrIsExplicitlyPending()
        {
            var catalog = LoadShippingCatalog();
            var covered = new HashSet<StatusEffectKind>(
                catalog.Entries
                    .Where(entry => entry != null && entry.Prefabs != null && entry.Prefabs.Any(p => p != null))
                    .Where(entry => entry.MatchStatusKind || entry.Loop)
                    .Select(entry => entry.StatusKind));

            var gaps = new List<string>();
            var staleWaivers = new List<string>();
            foreach (StatusEffectKind kind in System.Enum.GetValues(typeof(StatusEffectKind)))
            {
                // 이로운 것·표시 전용(미지·은신·수호·무적·힘·등불)은 저마다 다른 연출 어휘를 쓴다.
                if (StatusEffectInfo.GetPolarity(kind) != StatusEffectPolarity.Debuff)
                {
                    continue;
                }

                var isPending = System.Array.IndexOf(PendingStatusVfxKinds, kind) >= 0;
                if (covered.Contains(kind))
                {
                    if (isPending)
                    {
                        staleWaivers.Add(
                            $"{StatusEffectInfo.DisplayName(kind)}: VFX가 들어왔다 — PendingStatusVfxKinds에서 지워 게이트가 다시 물게 한다.");
                    }

                    continue;
                }

                if (!isPending)
                {
                    gaps.Add($"{StatusEffectInfo.DisplayName(kind)}({kind}): 걸려도 화면에 아무 일도 안 일어난다.");
                }
            }

            Assert.That(gaps, Is.Empty, string.Join("\n", gaps));
            Assert.That(staleWaivers, Is.Empty, string.Join("\n", staleWaivers));
        }
    }
}
#endif
